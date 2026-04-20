using System;
using System.IO;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.ASE
{
    /// <summary>
    /// v2.10.0 — split an ASE tri-panel scan (<c>lineDataType == 3</c>
    /// <see cref="AseFormatDecoder.LineDataTypeParcelDualEnergyBitmap"/>) into
    /// its three constituent channels — LowEnergy, HighEnergy, Material —
    /// and return them in the same shape <see cref="FS6000FormatDecoder.DecodedFs6000"/>
    /// uses, so <see cref="FS6000ModeRenderer"/> and the ROI-inspector code
    /// can be reused verbatim for ASE tri-panel scans.
    ///
    /// ASE tri-panel layout (port of Python <c>composite_ase_tri_panel</c> in
    /// <c>services/image-splitter/inspector/composite.py</c>):
    ///
    /// <code>
    ///   panel 0 : low-energy  (ushort, width/3 × height, LE)
    ///   panel 1 : high-energy (ushort, width/3 × height, LE)
    ///   panel 2 : material    (ushort sparse, width/3 × height — needs re-scale to 8-bit)
    /// </code>
    ///
    /// The three panels are stacked horizontally in the source blob — so a
    /// 1632×1558 tri-panel image has 544×1558 per-panel panels. Splitting
    /// is a single pass per row with <c>ReadOnlySpan&lt;ushort&gt;.CopyTo</c>.
    ///
    /// Material re-scaling: ASE panel 2 is 16-bit sparse (the scanner stores
    /// class values in a wide range rather than 0..255). Python's reference
    /// implementation renormalises via <c>clip(mat16 / max(mat16.max(),1) * 255, 0, 255)</c>
    /// before the LUT lookup. We mirror that here so downstream code sees the
    /// same 8-bit class indices FS6000 already produces natively.
    ///
    /// <b>Background:</b> the current <c>AsePercentileRenderer</c> flattens
    /// tri-panel blobs into a single wide grayscale — a silent correctness bug
    /// (~8% of production ASE scans are <c>ldt=3</c>). This class is both (a)
    /// the enabling plumbing for mode-catalog parity and (b) the fix for that
    /// bug: callers can now access the three panels as distinct channels.
    /// </summary>
    public static class AseTriPanelDecoder
    {
        /// <summary>
        /// Convert a tri-panel <see cref="AseFormatDecoder.DecodedAse"/> into
        /// a <see cref="FS6000FormatDecoder.DecodedFs6000"/>-shaped view so
        /// the FS6000 mode renderer / ROI inspector can process ASE tri-panel
        /// data without any further refactor.
        /// </summary>
        /// <param name="ase">Must have <c>IsMultiPanel == true</c>.</param>
        /// <param name="scanTimestamp">Carried through onto the output struct's <c>Timestamp</c> so the caller can trace the scan.</param>
        /// <exception cref="InvalidDataException">Thrown when the input is single-view (<c>lineDataType == 2</c>) or has a width not evenly divisible by 3.</exception>
        public static FS6000FormatDecoder.DecodedFs6000 SplitToDualEnergyShape(
            AseFormatDecoder.DecodedAse ase,
            DateTime? scanTimestamp = null)
        {
            if (!ase.IsMultiPanel)
            {
                throw new InvalidDataException(
                    $"AseTriPanelDecoder.SplitToDualEnergyShape requires lineDataType == 3 " +
                    $"(tri-panel / ParcelDualEnergyBitmap); got lineDataType == {ase.LineDataType}. " +
                    $"Single-view ASE blobs have no separable energies or material layer and can't be used for mode-catalog rendering.");
            }
            if (ase.Width % 3 != 0)
            {
                throw new InvalidDataException(
                    $"ASE tri-panel width {ase.Width} is not divisible by 3 — can't split into equal panels. " +
                    $"This is unusual; check the source blob's header.");
            }

            int panelW = ase.Width / 3;
            int h = ase.Height;
            int totalW = ase.Width;
            int panelPixels = panelW * h;

            // Allocate three destination buffers in the shape FS6000ModeRenderer
            // expects. Row-by-row copy avoids the dummy intermediate allocation
            // that a full-image column slice would require.
            var low = new ushort[panelPixels];
            var high = new ushort[panelPixels];
            var mat16 = new ushort[panelPixels];

            var src = ase.Pixels.AsSpan();
            for (int r = 0; r < h; r++)
            {
                int srcRow = r * totalW;
                int dstRow = r * panelW;
                src.Slice(srcRow + 0 * panelW, panelW).CopyTo(low.AsSpan(dstRow, panelW));
                src.Slice(srcRow + 1 * panelW, panelW).CopyTo(high.AsSpan(dstRow, panelW));
                src.Slice(srcRow + 2 * panelW, panelW).CopyTo(mat16.AsSpan(dstRow, panelW));
            }

            // Re-scale the 16-bit sparse material panel to 8-bit class indices.
            // Matches the Python reference in composite_ase_tri_panel:
            //   mat_8 = clip(mat16 / max(mat16.max(), 1) * 255, 0, 255).astype(uint8)
            // For flat / empty images (max == 0) we emit all zeros (background)
            // rather than dividing by zero.
            ushort matMax = 0;
            for (int i = 0; i < mat16.Length; i++)
            {
                if (mat16[i] > matMax) matMax = mat16[i];
            }
            var material8 = new byte[panelPixels];
            if (matMax > 0)
            {
                float scale = 255.0f / matMax;
                for (int i = 0; i < mat16.Length; i++)
                {
                    int v = (int)(mat16[i] * scale);
                    if (v < 0) v = 0;
                    else if (v > 255) v = 255;
                    material8[i] = (byte)v;
                }
            }
            // else: material8 is all zeros (background class), matches default

            return new FS6000FormatDecoder.DecodedFs6000(
                width: panelW,
                height: h,
                high: high,
                low: low,
                material: material8,
                timestamp: scanTimestamp);
        }
    }
}
