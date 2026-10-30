// Usage: dotnet run tests/fixtures/legacy-workbook/make-sample-workbook.cs -- "<output.xlsx>"
#:package ClosedXML
#:package SkiaSharp
using ClosedXML.Excel;
using SkiaSharp;

// A sample workbook in the legacy layout (Appendix C) with neutral data, for the S27 end-to-end check.
byte[] Png(SKColor color, string label)
{
    using var bitmap = new SKBitmap(900, 560);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(color);
    using var font = new SKFont(SKTypeface.Default, 48);
    using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
    canvas.DrawText(label, 40, 120, SKTextAlign.Left, font, paint);
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

using var book = new XLWorkbook();
string[] headers = ["Sr. No", "Issue", "Severity | Priority", "Screenshot", "Resolve By", "Is Complete", "Pending Reason", "Valid/Invalid", "Remarks"];
void Header(IXLWorksheet ws, int row) { for (var c = 0; c < headers.Length; c++) ws.Cell(row, c + 1).Value = headers[c]; }

var sd = book.AddWorksheet("Sales & Distribution");
sd.Cell(1, 2).Value = "Sample Testing Sheet";
Header(sd, 2);
sd.Cell(3, 1).Value = 1;
sd.Cell(3, 2).Value = "1. Quotation total ignores freight\nIssue: Create a quotation with freight charges\nActual Result: Total excludes freight\nExpected Result: Total includes freight\nImpact: Customers are under-quoted";
sd.Cell(3, 3).Value = "Severity: High<br>Priority: High";
sd.Cell(3, 5).Value = "Emily";
sd.Cell(3, 6).Value = "No";
sd.Row(3).Height = 90;
using (var s = new MemoryStream(Png(new SKColor(0x4F, 0x46, 0xE5), "Quotation screen"))) sd.AddPicture(s, ClosedXML.Excel.Drawings.XLPictureFormat.Png).MoveTo(sd.Cell(3, 4)).WithSize(180, 112);
sd.Cell(4, 1).Value = 2;
sd.Cell(4, 2).Value = "2. Delivery date accepts past dates";
sd.Cell(5, 2).Value = "Actual Result:";
sd.Cell(6, 2).Value = "A delivery date before the order date is saved";
sd.Cell(4, 3).Value = "Severity: Not yet assessed\nPriority: Pending…";
sd.Cell(4, 6).Value = "Yes";
sd.Cell(4, 9).Value = "For your refer.";
sd.Cell(7, 1).Value = 3;
sd.Cell(7, 2).Value = "3. Duplicate customer code allowed\nActual Result: Two customers share one code";
sd.Cell(7, 3).Value = "Severity: Medium\nPriority: Low";
sd.Cell(7, 8).Value = "Invalid";
sd.Cell(8, 1).Value = 4;
sd.Cell(8, 2).Value = "4. Sales register is slow\nActual Result: Takes 2 minutes for one month";
sd.Cell(8, 3).Value = "Severity: Low\nPriority: Low";
sd.Cell(8, 7).Value = "Waiting for production data";

var items = book.AddWorksheet("Item Mast, BOM, Tax Str.Mast");
Header(items, 1);
items.Cell(2, 1).Value = 1;
items.Cell(2, 2).Value = "1. BOM saves twice\nActual Result: Two rows after one save\nExpected Result: One row";
items.Cell(2, 3).Value = "Severity: Critical | Priority: High";
items.Cell(2, 5).Value = "Priya";
items.Row(2).Height = 90;
using (var s = new MemoryStream(Png(new SKColor(0xDC, 0x26, 0x26), "BOM list"))) items.AddPicture(s, ClosedXML.Excel.Drawings.XLPictureFormat.Png).MoveTo(items.Cell(2, 4)).WithSize(180, 112);

book.SaveAs(args[0]);
Console.WriteLine("written " + args[0]);
