using System.IO.Compression;
using System.Text;
using QaTracker.Application.Features.Attachments;

namespace QaTracker.Application.Tests;

public sealed class FileInspectorTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private static byte[] Zip(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write("<x/>");
            }
        }

        return buffer.ToArray();
    }

    [Theory]
    [InlineData("screenshot.png", "image/png")]
    [InlineData("SCREENSHOT.PNG", "image/png")]
    public void Png_IsAccepted(string name, string contentType) =>
        FileInspector.Inspect(name, PngHeader).Kind!.ContentType.ShouldBe(contentType);

    [Fact]
    public void JpegWithJpegExtension_IsAccepted() =>
        FileInspector.Inspect("photo.jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]).Kind.ShouldBe(FileInspector.Jpeg);

    [Fact]
    public void Pdf_IsAccepted() =>
        FileInspector.Inspect("spec.pdf", "%PDF-1.7\n..."u8).Kind.ShouldBe(FileInspector.Pdf);

    [Fact]
    public void ExecutableRenamedToPng_IsRejected()
    {
        var (kind, error) = FileInspector.Inspect("invoice.png", "MZ\x90\0\x03\0\0\0"u8);
        kind.ShouldBeNull();
        error!.ShouldContain("does not look like a PNG");
    }

    [Fact]
    public void PngNamedPdf_IsRejected() => FileInspector.Inspect("report.pdf", PngHeader).Kind.ShouldBeNull();

    [Fact]
    public void UnknownExtension_IsRejected() =>
        FileInspector.Inspect("setup.exe", "MZ"u8).Error!.ShouldContain("only");

    [Fact]
    public void WordAndExcel_AreRecognisedFromTheirContent()
    {
        FileInspector.Inspect("notes.docx", Zip("[Content_Types].xml", "word/document.xml")).Kind.ShouldBe(FileInspector.Docx);
        FileInspector.Inspect("data.xlsx", Zip("[Content_Types].xml", "xl/workbook.xml")).Kind.ShouldBe(FileInspector.Xlsx);
        FileInspector.Inspect("data.xlsx", Zip("[Content_Types].xml", "word/document.xml")).Kind.ShouldBeNull();
        FileInspector.Inspect("archive.docx", Zip("readme.txt")).Kind.ShouldBeNull();
    }

    [Fact]
    public void Utf8Text_IsAccepted_ButBinaryIsNot()
    {
        FileInspector.Inspect("log.txt", Encoding.UTF8.GetBytes("Exception at line 42 – null value")).Kind.ShouldBe(FileInspector.Text);
        FileInspector.Inspect("log.txt", [0x41, 0x00, 0x42]).Kind.ShouldBeNull();
        FileInspector.Inspect("log.txt", [0xC3, 0x28]).Kind.ShouldBeNull();
    }

    [Fact]
    public void EmptyAndOversizedFiles_AreRejected()
    {
        FileInspector.Inspect("a.txt", []).Error!.ShouldContain("empty");
        var big = new byte[FileInspector.MaxBytes + 1];
        PngHeader.CopyTo(big, 0);
        FileInspector.Inspect("big.png", big).Error!.ShouldContain("10 MB");
    }

    [Theory]
    [InlineData(@"C:\Users\me\Desktop\bug.png", "bug.png")]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("re<po>rt?.png", "report.png")]
    [InlineData(".png", "attachment.png")]
    public void FileNames_AreCleaned(string name, string expected) =>
        FileInspector.CleanFileName(name, FileInspector.Png).ShouldBe(expected);
}
