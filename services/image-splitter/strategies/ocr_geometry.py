"""
Strategy 2: OCR + Geometry

Container numbers follow ISO 6346 format: 4 uppercase letters + 7 digits (e.g., MSKU1234567).
If OCR detects two container numbers in the image, the split point is the midpoint
between their X positions, refined by the local density profile.

This strategy:
1. Runs Tesseract OCR on the image
2. Searches for ISO 6346 container number patterns
3. If 2+ found, calculates the midpoint between them
4. Optionally refines with local density analysis around the midpoint
"""

import time
import re
from typing import Optional
import numpy as np
import cv2

from strategies.base import BaseSplitStrategy, SplitResult

# ISO 6346 container number pattern: 4 letters + 7 digits
CONTAINER_PATTERN = re.compile(r'[A-Z]{4}\s*\d{7}')


class OCRGeometryStrategy(BaseSplitStrategy):

    @property
    def name(self) -> str:
        return "ocr_geometry"

    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        start = time.time()

        try:
            import pytesseract
        except ImportError:
            return None

        # Convert to grayscale if needed
        if len(image_array.shape) == 3:
            gray = cv2.cvtColor(image_array, cv2.COLOR_BGR2GRAY)
        else:
            gray = image_array

        h, w = gray.shape

        # Enhance contrast for OCR
        clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
        enhanced = clahe.apply(gray)

        # Run OCR with position data
        try:
            ocr_data = pytesseract.image_to_data(
                enhanced,
                output_type=pytesseract.Output.DICT,
                config='--psm 11 --oem 3'  # sparse text, best engine
            )
        except Exception as e:
            return SplitResult(
                strategy_name=self.name,
                split_x=w // 2,
                confidence=0.0,
                processing_ms=int((time.time() - start) * 1000),
                metadata={"error": str(e), "method": "ocr_failed"}
            )

        # Find container number matches with their X positions
        container_hits = []
        n_boxes = len(ocr_data['text'])

        # Build full text with position tracking
        for i in range(n_boxes):
            text = str(ocr_data['text'][i]).strip().upper()
            conf = int(ocr_data['conf'][i]) if ocr_data['conf'][i] != '-1' else 0

            if conf < 30 or len(text) < 4:
                continue

            # Check if this text contains a container number pattern
            matches = CONTAINER_PATTERN.findall(text)
            if matches:
                x = ocr_data['left'][i]
                y = ocr_data['top'][i]
                box_w = ocr_data['width'][i]
                center_x = x + box_w // 2
                container_hits.append({
                    'number': matches[0].replace(' ', ''),
                    'x': center_x,
                    'y': y,
                    'confidence': conf / 100.0,
                    'text': text
                })

        # Also try scanning for partial patterns in adjacent text boxes
        if len(container_hits) < 2:
            # Concatenate nearby text blocks and re-scan
            container_hits.extend(self._scan_adjacent_blocks(ocr_data, n_boxes, w))

        # Deduplicate by container number
        seen = set()
        unique_hits = []
        for hit in container_hits:
            num = hit['number'][:11]  # first 11 chars of container number
            if num not in seen:
                seen.add(num)
                unique_hits.append(hit)

        elapsed_ms = int((time.time() - start) * 1000)

        if len(unique_hits) < 2:
            return SplitResult(
                strategy_name=self.name,
                split_x=w // 2,
                confidence=0.1,
                processing_ms=elapsed_ms,
                metadata={
                    "method": "insufficient_ocr",
                    "hits_found": len(unique_hits),
                    "container_numbers": [h['number'] for h in unique_hits]
                }
            )

        # Sort by X position (left to right)
        unique_hits.sort(key=lambda h: h['x'])

        # Split point = midpoint between the two container number positions
        x1 = unique_hits[0]['x']
        x2 = unique_hits[1]['x']
        split_x = (x1 + x2) // 2

        # Confidence based on OCR quality and separation distance
        avg_conf = (unique_hits[0]['confidence'] + unique_hits[1]['confidence']) / 2
        separation = abs(x2 - x1) / w  # as fraction of image width
        separation_score = min(separation * 2, 1.0)  # good if >50% apart

        confidence = avg_conf * 0.6 + separation_score * 0.4

        return SplitResult(
            strategy_name=self.name,
            split_x=int(split_x),
            confidence=min(confidence, 1.0),
            processing_ms=elapsed_ms,
            metadata={
                "method": "dual_ocr",
                "container_1": unique_hits[0]['number'],
                "container_1_x": unique_hits[0]['x'],
                "container_2": unique_hits[1]['number'],
                "container_2_x": unique_hits[1]['x'],
                "all_hits": len(container_hits),
                "image_width": w,
                "image_height": h
            }
        )

    def _scan_adjacent_blocks(self, ocr_data: dict, n_boxes: int, img_width: int) -> list:
        """Scan adjacent text blocks for container numbers split across boxes."""
        hits = []
        texts_with_pos = []

        for i in range(n_boxes):
            text = str(ocr_data['text'][i]).strip().upper()
            conf = int(ocr_data['conf'][i]) if ocr_data['conf'][i] != '-1' else 0
            if conf < 20 or not text:
                continue
            texts_with_pos.append({
                'text': text,
                'x': ocr_data['left'][i],
                'y': ocr_data['top'][i],
                'w': ocr_data['width'][i],
                'conf': conf
            })

        # Try concatenating horizontally adjacent blocks on similar Y positions
        for i, t1 in enumerate(texts_with_pos):
            for t2 in texts_with_pos[i + 1:i + 4]:
                # Check if on similar Y position (same line)
                if abs(t1['y'] - t2['y']) > 20:
                    continue
                # Check if horizontally close
                gap = t2['x'] - (t1['x'] + t1['w'])
                if gap < 0 or gap > 50:
                    continue

                combined = t1['text'] + t2['text']
                matches = CONTAINER_PATTERN.findall(combined)
                if matches:
                    center_x = (t1['x'] + t2['x'] + t2['w']) // 2
                    hits.append({
                        'number': matches[0].replace(' ', ''),
                        'x': center_x,
                        'y': t1['y'],
                        'confidence': min(t1['conf'], t2['conf']) / 100.0,
                        'text': combined
                    })

        return hits
