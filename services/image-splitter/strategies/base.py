from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Optional
import numpy as np


@dataclass
class SplitResult:
    """Result from a splitting strategy."""
    strategy_name: str
    split_x: int
    confidence: float  # 0.0 - 1.0
    processing_ms: int = 0
    metadata: dict = field(default_factory=dict)
    left_image: Optional[bytes] = None
    right_image: Optional[bytes] = None


class BaseSplitStrategy(ABC):
    """Abstract base class for image splitting strategies."""

    @property
    @abstractmethod
    def name(self) -> str:
        """Unique name for this strategy."""
        ...

    @abstractmethod
    async def analyze(self, image_data: bytes, image_array: np.ndarray) -> Optional[SplitResult]:
        """
        Analyze an image and find the optimal split point.

        Args:
            image_data: Raw image bytes (JPEG/PNG)
            image_array: Decoded numpy array (H, W, C) or (H, W) grayscale

        Returns:
            SplitResult with split_x coordinate, or None if no split detected
        """
        ...
