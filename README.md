# Utf8XmlReader

A high-performance, zero-allocation XML parser for UTF-8 encoded XML, inspired by the behavior of `System.Xml.XmlReader`.
Designed for scenarios where you desire minimal memory pressure by working directly with UTF-8 bytes instead of converting to .NET strings.

> [!NOTE]
> This is NOT a drop-in replacement for `System.Xml.XmlReader`, but for the subset of functionality that we support, the API is intended to feel familiar to those experienced with `XmlReader`.

## Why This Library?

### The Motivation
`System.Xml.XmlReader` works with UTF-16 strings, which means:
- **Unnecessary conversions**: UTF-8 text from the XML being read gets converted to UTF-16 strings
- **Heap allocations**: Every element name, attribute name, and text value creates a new string object
- **GC pressure**: Parsing large XML files generates significant garbage collection overhead

Utf8XmlReader is an attempt to eliminate these inefficiencies by:
- Working directly with UTF-8 bytes using `ReadOnlySpan<byte>`
- Zero heap allocations during parsing (uses stack-only `ref struct` types)
- Letting you decide when (or if) to convert UTF-8 to strings
- Using fixed-size buffers with predictable memory usage

## When to Use This Library

✅ **Intended for:**
- High-throughput XML processing (logs, feeds, telemetry)
- Processing large XML files with memory constraints
- Systems where GC pauses are problematic (real-time, low-latency services)
- UTF-8 pipelines where string conversion is wasteful

❌ **Not suitable for:**
- XML with namespace URI resolution
- DOM-style APIs or random access to XML structure
- XPath/XSLT processing
- Full XML validation and schema support

## Installation

```bash
# TODO: Add NuGet package once published
dotnet add package Utf8XmlReader
```

## Quick Start

### Basic XML Parsing

```csharp
using Utf8Xml;
using System.Xml;

// Your XML as UTF-8 bytes
byte[] xml = "<root><item id=\"1\">Hello</item></root>"u8.ToArray();

// Create reader and parse
var reader = Utf8XmlReader.Create(xml);
while (reader.Read())
{
    if (reader.NodeType == XmlNodeType.Element)
    {
        // Work with UTF-8 bytes directly (zero allocations)
        ReadOnlySpan<byte> name = reader.LocalName;
        
        // Or convert to string when needed
        Console.WriteLine($"Element: {reader.LocalNameAsString}");
        
        // Read attributes
        var idValue = reader.GetAttribute("id"u8);
        if (!idValue.IsEmpty)
        {
            Console.WriteLine($"  id: {Encoding.UTF8.GetString(idValue)}");
        }
    }
}
```

### Reading Element Content

```csharp
var xml = "<message>Hello, World!</message>"u8;
var reader = Utf8XmlReader.Create(xml);

reader.Read(); // Move to <message>
ReadOnlySpan<byte> content = reader.ReadContentAsBytes();
// content now contains "Hello, World!" as UTF-8 bytes
// reader is now positioned at </message>
```

### Streaming Large XML Files

Use `ElementChunker` to extract elements from large files without loading the entire document:

```csharp
using var stream = File.OpenRead("large-feed.xml");
using var chunker = new ElementChunker(stream, maxChunkSize: 131_072); // 128 KB

// Extract each <item> element as a chunk
while (chunker.TryGetChunk("item"u8, out ReadOnlyMemory<byte> chunk))
{
    // Parse individual chunk with Utf8XmlReader
    var reader = Utf8XmlReader.Create(chunk.Span);
    reader.Read(); // Move to <item>
    
    // Process this item...
    var title = reader.GetAttribute("title"u8);
}
```

The [unit tests](./tests/Utf8XmlReaderTests//Utf8XmlReaderTests.cs) provide some other usage examples.

## API Overview

### Utf8XmlReader

Main reader for in-memory UTF-8 XML buffers.

**Key Properties:**
- `NodeType`: Current node type (`Element`, `EndElement`, `Text`, etc.)
- `LocalName`: Element/attribute name as `ReadOnlySpan<byte>`
- `Depth`: Current nesting depth
- `IsEmptyElement`: True for self-closing elements

**Key Methods:**
- `Read()`: Advance to next node
- `GetAttribute(ReadOnlySpan<byte> name)`: Get attribute value as UTF-8 bytes
- `ReadContentAsBytes()`: Read element content and advance to end tag
- `SkipElement()`: Skip entire element subtree

### ElementChunker

Streaming chunker for large XML files.

**Constructor:**
```csharp
new ElementChunker(Stream utf8XmlStream, int maxChunkSize = 131_072)
```

**Methods:**
- `TryGetChunk(ReadOnlySpan<byte> elementName, out ReadOnlyMemory<byte> chunk)`: Synchronous extraction

## Performance Characteristics

- **Zero heap allocations** during parsing (when using span APIs)
- **Stack-only reader** (`ref struct`) prevents GC pressure
- **Fixed-size buffers** in ElementChunker for predictable memory usage
- **ArrayPool<byte>** for temporary buffers to minimize allocations
- **Forward-only parsing** for optimal speed

## Target Frameworks

- .NET 9.0
- .NET 10.0
- AOT compatible

## Examples

See the test projects for comprehensive examples:
- `tests/Utf8XmlReaderTests/Utf8XmlReaderTests.cs` - Core reader functionality
- `tests/Utf8XmlReaderTests/ElementChunkerTests.cs` - Streaming scenarios

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Contributing

Contributions will be welcome, but need to get some more protections, NuGet publishing, CI/CD, etc. in place first.
