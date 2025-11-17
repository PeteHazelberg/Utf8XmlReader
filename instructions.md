# Utf8XmlReader Project Instructions

## Core Design Goals

1. **Zero-Allocation Parsing**: Use `ref struct` and `ReadOnlySpan<byte>` to avoid heap allocations and GC pressure
2. **UTF-8 Native**: Work directly with UTF-8 bytes; return spans instead of strings; convert only when explicitly requested
3. **Low Latency**: Forward-only parsing, minimal transformations, `ArrayPool<byte>` for temporary buffers
4. **Predictability**: Fixed-size buffers that never grow; clear size limits; fail fast on malformed XML

## Components

**Utf8XmlReader**: Stack-only (`ref struct`) reader for in-memory UTF-8 XML. Returns `ReadOnlySpan<byte>` for names/content. No namespace awareness.

**ElementChunker**: Streams large XML files with fixed buffer. Extracts elements by name as `ReadOnlyMemory<byte>` chunks for processing with Utf8XmlReader.

## Development Guidelines

- Return `ReadOnlySpan<byte>` instead of strings
- Use spans/slices to reference buffers without copying
- Leverage `ArrayPool<byte>` for temporary buffers
- Let callers control UTF-8 to string conversion
- Throw exceptions for malformed XML (fail fast)
- Test UTF-8 multi-byte characters and buffer boundaries

## Non-Goals

No XML validation, namespace URI resolution, XPath/XSLT, DOM APIs, streaming writes, or backward navigation
