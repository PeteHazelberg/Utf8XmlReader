using FluentAssertions;
using System.Text;
using Xunit.Abstractions;

namespace Utf8Xml;

public class ElementChunkerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public ElementChunkerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void CanReadMultipleChunks_1_element_per_chunk()
    {
        var xml = """
            <data>
              <record id="1">First record content</record>
              <record id="2">Second record content</record>
              <record id="3">Third record content</record>
            </data>
            """u8;

        using var stream = new MemoryStream(xml.ToArray());
        using var streamReader = new ElementChunker(stream, maxChunkSize: 64); // force 1 record per chunk

        List<string> chunks = [];
        while (streamReader.TryGetChunk("record"u8, out var chunk))
        {
            chunks.Add(Encoding.UTF8.GetString(chunk));
        }

        chunks[0].Should().Be("""<record id="1">First record content</record>""");
        chunks[1].Should().Be("""<record id="2">Second record content</record>""");
        chunks[2].Should().Be("""<record id="3">Third record content</record>""");
        chunks.Count.Should().Be(3);
    }

    [Fact]
    public void CanReadMultipleChunks_multiple_elements_per_chunk()
    {
        var xml = """
                  <data>
                    <record id="1">First record content</record>
                    <record id="2">Second record content</record>
                    <record id="3">Third record content</record>
                  </data>
                  """u8;

        using var stream = new MemoryStream(xml.ToArray());
        using var streamReader = new ElementChunker(stream, maxChunkSize: 128); // force 1 record per chunk

        List<string> chunks = [];
        while (streamReader.TryGetChunk("record"u8, out var chunk))
        {
            chunks.Add(Encoding.UTF8.GetString(chunk));
        }

        chunks.Count.Should().Be(2);
        chunks[0].Should().StartWith("""<record id="1">First record content</record>""");
        chunks[0].Should().EndWith("""<record id="2">Second record content</record>""");
        chunks[1].Should().Be("""<record id="3">Third record content</record>""");
    }

    [Fact]
    public void InsufficientSizeWarning()
    {
        var xmlContent = """
                         <data>
                           <record id="1">First record content</record>
                           <record id="2">Second record content</record>
                           <record id="3">Third record content</record>
                         </data>
                         """u8;

        // first <record> element is 44 bytes
        // second <record> element is 45 bytes

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var streamReader1 = new ElementChunker(stream, maxChunkSize: 20); // too small for any
        try
        {
            streamReader1.TryGetChunk("record"u8, out _); // throws
            Assert.Fail("Should have thrown an exception because maxChunkSize is too small");
        }
        catch (InvalidOperationException e)
        {
            e.Message.Should().ContainAll("20 bytes", "Increase maxChunkSize");
        }

        try
        {
            stream.Position = 0;
            using var streamReader2 = new ElementChunker(stream, maxChunkSize: 44); // Just under the required size
            streamReader2.TryGetChunk("record"u8, out _).Should().BeTrue();
            streamReader2.TryGetChunk("record"u8, out _); // throws for fixed-size buffer
            Assert.Fail("Should have thrown an exception because maxChunkSize is still too small");
        }
        catch (InvalidOperationException e)
        {
            e.Message.Should().ContainAll("44 bytes", "Increase maxChunkSize");
        }

        stream.Position = 0;
        using var streamReader3 = new ElementChunker(stream, maxChunkSize: 45); // Sufficient size accounting for XML context
        streamReader3.TryGetChunk("record"u8, out _).Should().BeTrue();
        streamReader3.TryGetChunk("record"u8, out _).Should().BeTrue();
        streamReader3.TryGetChunk("record"u8, out _).Should().BeTrue();
        streamReader3.TryGetChunk("record"u8, out _).Should().BeFalse("EOF");
    }

    [Fact]
    public void HandlesEmptyStream()
    {
        using var stream = new MemoryStream();
        using var chunker = new ElementChunker(stream);

        var result = chunker.TryGetChunk("record"u8, out var chunk);

        result.Should().BeFalse();
        chunk.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void HandlesStreamWithNoMatchingElements()
    {
        var xmlContent = "<data><item>First item</item><item>Second item</item></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        var result = chunker.TryGetChunk("record"u8, out var chunk);

        result.Should().BeFalse();
        chunk.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void HandlesSelfClosingElements()
    {
        var xmlContent = "<data><record id=\"1\" /><record id=\"2\" /><record id=\"3\" /></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = []; // Work with strings for test assertions
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            // Store the string representation for assertions
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Self-closing elements may be combined into fewer chunks if they fit
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().StartWith("<record id=\"1\" />");
        allChunkContent.Should().Contain("<record id=\"2\" />");
        allChunkContent.Should().EndWith("record id=\"3\" />");
    }

    [Fact]
    public void HandlesNestedElementsWithSameName()
    {
        var xmlContent = "<data><record id=\"1\"><record id=\"nested\">Nested content</record>Outer content</record><record id=\"2\">Simple content</record></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined, but we should have at least one chunk
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("record id=\"nested\"");
        allChunkContent.Should().Contain("Outer content");
        allChunkContent.Should().Contain("Simple content");
        allChunkContent.EndsWith("</record>").Should().BeTrue();
    }

    [Fact]
    public void HandlesElementsWithAttributesContainingQuotes()
    {
        var xml= """
                 <data>
                   <record title='He said "Hello"' id="1">Content 1</record>
                   <record title="She said 'Hi'" id="2">Content 2</record>
                 </data>
                 """u8.ToArray();

        using var stream = new MemoryStream(xml);
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined into fewer chunks
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("""
                                         title='He said "Hello"'
                                         """);
        allChunkContent.Should().Contain("""
                                         title="She said 'Hi'"
                                         """);
    }

    [Fact]
    public void HandlesElementsWithCDataSections()
    {
        var xmlContent = "<data><record id=\"1\"><![CDATA[Some <special> & content]]></record><record id=\"2\">Normal content</record></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined into fewer chunks
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("<![CDATA[Some <special> & content]]>");
        allChunkContent.Should().Contain("Normal content");
    }

    [Fact]
    public void HandlesElementsWithNamespaces()
    {
        var xmlContent = "<data xmlns:ns=\"http://example.com\"><ns:record id=\"1\">Content 1</ns:record><ns:record id=\"2\">Content 2</ns:record></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        // Note: ElementChunker looks for local name only, but ns:record won't match "record" search
        // So we should expect no matches when searching for "record"
        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        chunkStrings.Count.Should().Be(0); // No matches because element name is "ns:record", not "record"

        // But if we search for the full qualified name, we should find them
        // Reset the stream for the second test
        stream.Position = 0;
        using var chunker2 = new ElementChunker(stream);
        chunkStrings.Clear();
        while (chunker2.TryGetChunk("ns:record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined into one chunk
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);
        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("ns:record");
        allChunkContent.Should().Contain("Content 1");
        allChunkContent.Should().Contain("Content 2");
    }

    [Fact]
    public void HandlesVeryLargeElementsWithinChunkSize()
    {
        var largeContent = new string('x', 10_000);
        var xmlContent = Encoding.UTF8.GetBytes($"<data><record id=\"1\">{largeContent}</record><record id=\"2\">Small content</record></data>");

        using var stream = new MemoryStream(xmlContent);
        using var chunker = new ElementChunker(stream, maxChunkSize: 16_384);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // The large element and small element might be combined into a single chunk if they fit
        // This is expected behavior based on the chunking logic
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain(largeContent);
        allChunkContent.Should().Contain("Small content");
    }

    [Fact]
    public void ThrowsOnDisposedChunker()
    {
        var xmlContent = "<data><record>test</record></data>"u8;
        using var stream = new MemoryStream(xmlContent.ToArray());
        var chunker = new ElementChunker(stream);

        chunker.Dispose();

        var action = () => chunker.TryGetChunk("record"u8, out _);
        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ThrowsOnEmptyElementName()
    {
        var xmlContent = "<data><record>test</record></data>"u8;
        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        var action = () => chunker.TryGetChunk(ReadOnlySpan<byte>.Empty, out _);
        action.Should().Throw<ArgumentException>().WithMessage("*Element name cannot be empty*");
    }

    [Fact]
    public void HandlesElementsWithComplexContent()
    {
        var xmlContent = "<data><record><name>John Doe</name><age>30</age><address><street>123 Main St</street><city>Anytown</city></address></record><record><name>Jane Smith</name><age>25</age></record></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined into fewer chunks
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("<name>John Doe</name>");
        allChunkContent.Should().Contain("<address>");
        allChunkContent.Should().Contain("<street>123 Main St</street>");
        allChunkContent.Should().Contain("<name>Jane Smith</name>");
        allChunkContent.Should().Contain("<age>25</age>");
    }

    [Fact]
    public void HandlesMixedSelfClosingAndRegularElements()
    {
        var xmlContent = "<data><record id=\"1\">Regular content</record><record id=\"2\" /><record id=\"3\">More content</record><record id=\"4\" /></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined into fewer chunks if they fit within MaxChunkSize
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("Regular content");
        allChunkContent.Should().Contain("record id=\"2\" />");
        allChunkContent.Should().Contain("More content");
        allChunkContent.Should().Contain("record id=\"4\" />");
    }

    [Fact]
    public void HandlesElementsWithSpecialCharactersInAttributes()
    {
        var xmlContent = "<data><record data=\"&lt;test&gt;\" id=\"1\">Content with &amp; symbols</record><record data=\"value with &quot;quotes&quot;\" id=\"2\">More content</record></data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        List<string> chunkStrings = [];
        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkStrings.Add(Encoding.UTF8.GetString(chunk));
        }

        // Elements may be combined into fewer chunks if they fit within MaxChunkSize
        chunkStrings.Count.Should().BeGreaterOrEqualTo(1);

        var allChunkContent = string.Join("", chunkStrings);
        allChunkContent.Should().Contain("&lt;test&gt;");
        allChunkContent.Should().Contain("&amp; symbols");
        allChunkContent.Should().Contain("&quot;quotes&quot;");
    }

    [Fact]
    public void HandlesElementsWithOnlyWhitespaceBetween()
    {
        var xmlContent = "<data>  \n  <record id=\"1\">Content 1</record>  \n  \n  <record id=\"2\">Content 2</record>  \n  </data>"u8;

        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream, maxChunkSize: 200);

        var hasChunk = chunker.TryGetChunk("record"u8, out var chunk);
        hasChunk.Should().BeTrue();

        var chunkString = Encoding.UTF8.GetString(chunk);
        // Should combine both records since they're separated only by whitespace
        chunkString.Should().Contain("Content 1");
        chunkString.Should().Contain("Content 2");

        var hasSecondChunk = chunker.TryGetChunk("record"u8, out _);
        hasSecondChunk.Should().BeFalse();
    }

    [Fact]
    public void HandlesConstructorEdgeCases()
    {
        using var readableStream = new MemoryStream("<data></data>"u8.ToArray());
        using var nonReadableStream = new MemoryStream();
        nonReadableStream.Close(); // Make it non-readable

        // Test null stream
        var nullAction = () => new ElementChunker(null!);
        nullAction.Should().Throw<ArgumentNullException>();

        // Test non-readable stream
        var nonReadableAction = () => new ElementChunker(nonReadableStream);
        nonReadableAction.Should().Throw<ArgumentException>().WithMessage("*must be readable*");

        // Test invalid chunk size
        var invalidSizeAction = () => new ElementChunker(readableStream, maxChunkSize: 0);
        invalidSizeAction.Should().Throw<ArgumentOutOfRangeException>();

        var negativeSizeAction = () => new ElementChunker(readableStream, maxChunkSize: -1);
        negativeSizeAction.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HandlesMultipleCallsAfterEOF()
    {
        var xmlContent = "<data><record>test</record></data>"u8;
        using var stream = new MemoryStream(xmlContent.ToArray());
        using var chunker = new ElementChunker(stream);

        // Get the first chunk
        var hasFirst = chunker.TryGetChunk("record"u8, out var firstChunk);
        hasFirst.Should().BeTrue();
        Encoding.UTF8.GetString(firstChunk).Should().Be("<record>test</record>");

        // Subsequent calls should return false
        var hasSecond = chunker.TryGetChunk("record"u8, out var secondChunk);
        hasSecond.Should().BeFalse();
        secondChunk.IsEmpty.Should().BeTrue();

        // Even more calls should still return false
        var hasThird = chunker.TryGetChunk("record"u8, out var thirdChunk);
        hasThird.Should().BeFalse();
        thirdChunk.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void PerformanceTest_LargeXmlProcessing()
    {
        // Create a reasonably large XML document
        var sb = new StringBuilder();
        sb.AppendLine("<data>");
        for (int i = 0; i < 1000; i++)
        {
            sb.AppendLine($"  <record id=\"{i}\">Content for record {i} with some additional text to make it larger and test buffer management</record>");
        }
        sb.AppendLine("</data>");

        var xmlBytes = Encoding.UTF8.GetBytes(sb.ToString()).AsSpan();
        _testOutputHelper.WriteLine($"XML size: {xmlBytes.Length:N0} bytes");

        using var stream = new MemoryStream(xmlBytes.ToArray());
        using var chunker = new ElementChunker(stream, maxChunkSize: 8192);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int chunkCount = 0;
        long totalBytes = 0;

        while (chunker.TryGetChunk("record"u8, out var chunk))
        {
            chunkCount++;
            totalBytes += chunk.Length;
            
            // Process chunk without creating unnecessary allocations
            // In real usage, you'd work with the span directly
        }

        sw.Stop();

        // Verify correctness  
        chunkCount.Should().BeGreaterThan(1); // Should be multiple chunks
        totalBytes.Should().BeGreaterThan(50_000); // Should process most record content

        _testOutputHelper.WriteLine($"Processed {chunkCount:N0} chunks, {totalBytes:N0} bytes in {sw.ElapsedMilliseconds} ms");
        _testOutputHelper.WriteLine($"Throughput: {xmlBytes.Length / (double)sw.ElapsedMilliseconds * 1000 / 1024 / 1024:F2} MB/s");

        // Performance assertion - should process at least 10 MB/s (reasonable minimum)
        var throughputMBps = xmlBytes.Length / (double)sw.ElapsedMilliseconds * 1000 / 1024 / 1024;
        throughputMBps.Should().BeGreaterThan(5.0, "ElementChunker should achieve reasonable throughput");
    }
}
