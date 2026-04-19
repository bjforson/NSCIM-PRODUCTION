using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Unified data-export service shared between desktop (NickScanWebApp.New)
    /// and mobile. Supports CSV, Excel (ClosedXML), and PDF (QuestPDF) with a
    /// consistent call signature: headers array + row mapper. Pages can swap
    /// between formats by changing one method name.
    /// </summary>
    public class ExportService
    {
        static ExportService()
        {
            // QuestPDF community license — free for orgs under $1M revenue.
            // Idempotent so safe to set in the static constructor.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // ── CSV ──────────────────────────────────────────────────────

        /// <summary>Export data to CSV (RFC 4180, UTF-8 with BOM for Excel compatibility).</summary>
        public byte[] ExportToCSV<T>(IEnumerable<T> data, string[] headers, Func<T, string[]> rowMapper)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCSV)));
            foreach (var item in data)
            {
                var values = rowMapper(item);
                sb.AppendLine(string.Join(",", values.Select(EscapeCSV)));
            }
            // Prepend UTF-8 BOM so Excel opens non-ASCII correctly
            var bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var result = new byte[bom.Length + bodyBytes.Length];
            Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
            Buffer.BlockCopy(bodyBytes, 0, result, bom.Length, bodyBytes.Length);
            return result;
        }

        // ── Excel ────────────────────────────────────────────────────

        /// <summary>
        /// Export data to an .xlsx file with header row, auto-sized columns,
        /// frozen header, auto-filter, and NSCIS navy header styling.
        /// Sheet name is sanitized to Excel's rules.
        /// </summary>
        public byte[] ExportToExcel<T>(
            IEnumerable<T> data,
            string[] headers,
            Func<T, string[]> rowMapper,
            string sheetName = "Data",
            string? title = null)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add(SanitizeSheetName(sheetName));

            int row = 1;

            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleRange = ws.Range(1, 1, 1, Math.Max(headers.Length, 1));
                titleRange.Merge();
                titleRange.Value = title;
                titleRange.Style.Font.Bold = true;
                titleRange.Style.Font.FontSize = 14;
                titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                row++;
                row++; // blank row
            }

            int headerRow = row;
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(headerRow, c + 1).Value = headers[c];
            }
            var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            row = headerRow + 1;

            foreach (var item in data)
            {
                var values = rowMapper(item);
                for (int c = 0; c < values.Length && c < headers.Length; c++)
                {
                    ws.Cell(row, c + 1).Value = values[c];
                }
                row++;
            }

            ws.SheetView.FreezeRows(headerRow);

            // AutoFit columns (cheap on small tables)
            if (row - headerRow < 10000)
            {
                ws.Columns().AdjustToContents();
            }

            if (row > headerRow + 1)
            {
                ws.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // ── PDF ──────────────────────────────────────────────────────

        /// <summary>
        /// Export data to a landscape A4 PDF with NSCIS branding, timestamp,
        /// paginated table with zebra striping, and page numbers.
        /// </summary>
        public byte[] ExportToPDF<T>(
            IEnumerable<T> data,
            string[] headers,
            Func<T, string[]> rowMapper,
            string title = "NSCIS Report",
            string? subtitle = null)
        {
            var rows = data.Select(rowMapper).ToList();
            var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(col =>
                    {
                        col.Item().Background("#1a237e").Padding(8).Row(row =>
                        {
                            row.RelativeItem().Column(inner =>
                            {
                                inner.Item().Text("NICKSCAN CENTRAL IMAGING SYSTEM")
                                    .FontColor(Colors.White).FontSize(10).Bold();
                                inner.Item().Text(title)
                                    .FontColor(Colors.White).FontSize(14).Bold();
                                if (!string.IsNullOrWhiteSpace(subtitle))
                                {
                                    inner.Item().Text(subtitle!)
                                        .FontColor("#e3e3e3").FontSize(9);
                                }
                            });
                            row.ConstantItem(200).AlignRight().Column(inner =>
                            {
                                inner.Item().AlignRight().Text($"Generated: {generatedAt}")
                                    .FontColor("#e3e3e3").FontSize(8);
                                inner.Item().AlignRight().Text($"Rows: {rows.Count:N0}")
                                    .FontColor("#e3e3e3").FontSize(8);
                            });
                        });
                    });

                    page.Content().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var _ in headers)
                            {
                                columns.RelativeColumn();
                            }
                        });

                        table.Header(header =>
                        {
                            foreach (var h in headers)
                            {
                                header.Cell().Background("#e8eaf6").Border(0.5f).BorderColor(Colors.Grey.Lighten1)
                                    .Padding(4).Text(h).Bold().FontSize(9);
                            }
                        });

                        for (int r = 0; r < rows.Count; r++)
                        {
                            var stripe = r % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                            foreach (var cell in rows[r])
                            {
                                table.Cell().Background(stripe).Border(0.25f).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(3).Text(cell ?? "").FontSize(8);
                            }
                            for (int c = rows[r].Length; c < headers.Length; c++)
                            {
                                table.Cell().Background(stripe).Border(0.25f).BorderColor(Colors.Grey.Lighten2)
                                    .Padding(3).Text("").FontSize(8);
                            }
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                        x.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
                        x.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });

            return doc.GeneratePdf();
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>Generate an export filename with timestamp and extension.</summary>
        public string GenerateFilename(string prefix, string extension = "csv")
        {
            return $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
        }

        private string EscapeCSV(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Data";
            var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }
            var sanitized = sb.ToString();
            if (sanitized.Length > 31) sanitized = sanitized.Substring(0, 31);
            return sanitized;
        }
    }
}
