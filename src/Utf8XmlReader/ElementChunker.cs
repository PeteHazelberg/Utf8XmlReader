using System.Buffers;
using System.Text;
using static Utf8Xml.XmlCharacters;

namespace Utf8Xml;

/// <summary>
/// Scans UTF-8 XML streams and extracts element-based chunks without loading the entire document into RAM.
/// </summary>
/// <remarks>
/// ElementChunker is designed for efficient, forward-only processing of UTF-8 encoded XML streams. It uses
/// a fixed-size buffer that never resizes or grows. This design prioritizes predictable memory usage and
/// simplicity over handling arbitrarily large elements. The class does not fully validate XML; it only 
/// parses enough to locate and extract the specified elements.
/// </remarks>
/// <param name="utf8XmlStream">The input stream containing UTF-8 encoded XML data.</param>
/// <param name="maxChunkSize">The fixed buffer size (in bytes). This determines both the maximum chunk size 
/// and the internal buffer size. Any XML Elements found that are larger than this will cause an exception.</param>
public sealed class ElementChunker : IDisposable
{
    private readonly Stream _stream;
    private readonly int _fixedBufferSize;
    private byte[] _buffer; // FIXED-SIZE working buffer (rented from ArrayPool)
    private int _bufferUsed; // Number of valid bytes currently in buffer
    private int _bufferPosition; // Position in buffer where we last left off
    private bool _eof; // End of stream reached
    private bool _disposed;

    /// <summary>
    /// Creates a new ElementChunker with a fixed-size buffer.
    /// </summary>
    /// <param name="utf8XmlStream">The UTF-8 XML stream to scan for elements</param>
    /// <param name="maxChunkSize">
    /// The fixed buffer size in bytes. This determines the maximum element size that can be processed.
    /// </param>
    public ElementChunker(Stream utf8XmlStream, int maxChunkSize = 131_072) // 128 kilobytes
    {
        ArgumentNullException.ThrowIfNull(utf8XmlStream);
        if (!utf8XmlStream.CanRead) throw new ArgumentException("Stream must be readable", nameof(utf8XmlStream));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkSize);
        
        _stream = utf8XmlStream;
        _fixedBufferSize = maxChunkSize;
        
        // FIXED-SIZE BUFFER: Allocate exactly the requested size, never resize
        _buffer = ArrayPool<byte>.Shared.Rent(maxChunkSize);
        _bufferUsed = 0;
        _bufferPosition = 0;
        _eof = false;
    }

    /// <summary>
    /// Retrieves a chunk containing one or more complete elements matching <paramref name="elementName"/>.
    /// Each returned chunk starts at an opening tag for the requested element and ends after the corresponding closing tag.
    /// If multiple matching elements fit contiguously within the buffer, they are combined.
    /// </summary>
    /// <param name="elementName">Element local name (case-sensitive) as UTF-8 bytes.</param>
    /// <param name="chunk">Span over internal buffer containing the chunk. Valid until next call.</param>
    /// <returns>true if a chunk was produced; false if end of stream and no more elements.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a single element exceeds the max chunk size.</exception>
    public bool TryGetChunk(ReadOnlySpan<byte> elementName, out ReadOnlySpan<byte> chunk)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ElementChunker));
        if (elementName.IsEmpty) throw new ArgumentException("Element name cannot be empty", nameof(elementName));

        chunk = default;

        while (true)
        {
            // Make sure we have data to work with
            EnsureBufferHasData();

            // Find the next opening tag for our element
            int openIndex = FindNextOpeningTag(elementName);
            if (openIndex == -1)
            {
                if (_eof) return false;
                if (!ReadMoreData()) return false;
                continue;
            }

            // Find the end of this element
            int elementEnd = FindElementEnd(openIndex, elementName);
            if (elementEnd == -1)
            {
                if (_eof) return false;
                if (!ReadMoreData()) return false;
                continue;
            }

            int elementSize = elementEnd - openIndex;
            if (elementSize > _fixedBufferSize)
            {
                throw new InvalidOperationException(
                   $"Fixed buffer size {_fixedBufferSize} bytes is too small to fit one of the " +
                   $"'{Encoding.UTF8.GetString(elementName)}' elements. " +
                   $"Need at least {elementSize} bytes. Increase maxChunkSize in the constructor.");
            }

            // Start building the chunk
            int chunkStart = openIndex;
            int chunkEnd = elementEnd;

            // Try to include additional consecutive elements within the buffer constraints
            while (true)
            {
                // Skip whitespace after current element
                int nextPos = SkipWhitespaceFrom(chunkEnd);

                // If we've reached the end of buffer data, we can't add more elements safely
                if (nextPos >= _bufferUsed)
                {
                    break;
                }

                // Look for next element
                int nextOpenIndex = FindOpeningTagAt(nextPos, elementName);
                if (nextOpenIndex != nextPos)
                {
                    break; // No element at this position
                }

                int nextElementEnd = FindElementEnd(nextOpenIndex, elementName);
                if (nextElementEnd == -1)
                {
                    break; // Element extends beyond current buffer
                }

                int newChunkSize = nextElementEnd - chunkStart;
                if (newChunkSize > _fixedBufferSize)
                {
                    break; // Would exceed our fixed buffer size limit
                }

                chunkEnd = nextElementEnd;
            }

            chunk = _buffer.AsSpan(chunkStart, chunkEnd - chunkStart);
            _bufferPosition = chunkEnd;
            return true;
        }
    }

    /// <summary>
    /// Ensures the buffer contains data, reading from stream if necessary.
    /// </summary>
    private void EnsureBufferHasData()
    {
        if (_bufferUsed == 0 || _bufferPosition >= _bufferUsed)
        {
            ReadMoreData();
        }
    }

    /// <summary>
    /// Reads more data from the stream into our FIXED-SIZE buffer.
    /// Compacts existing data if needed, but NEVER resizes the buffer.
    /// </summary>
    private bool ReadMoreData()
    {
        if (_eof) return false;

        // If needed, move next element to the beginning of the buffer
        if (_bufferPosition > 0)
        {
            int remainingBytes = _bufferUsed - _bufferPosition;
            if (remainingBytes > 0)
            {
                var sourceSpan = _buffer.AsSpan(_bufferPosition, remainingBytes);
                var destinationSpan = _buffer.AsSpan(0, remainingBytes);
                sourceSpan.CopyTo(destinationSpan);
            }
            _bufferUsed = remainingBytes;
            _bufferPosition = 0;
        }

        int availableSpace = _buffer.Length - _bufferUsed;
        if (availableSpace == 0)
        {
            // FIXED-SIZE BUFFER: Cannot grow buffer - this is by design!
            throw new InvalidOperationException(
                $"Fixed buffer is full ({_fixedBufferSize} bytes) and cannot read more data. " +
                $"The current element or combination of elements exceeds the buffer size. " +
                $"Increase maxChunkSize in the constructor to process these larger XML elements.");
        }

        // Read more data into our buffer
        int bytesRead = _stream.Read(_buffer, _bufferUsed, availableSpace);
        _bufferUsed += bytesRead;
        if (bytesRead == 0)
        {
            _eof = true;
            return _bufferUsed > _bufferPosition;
        }
        return true;
    }

    /// <summary>
    /// Efficiently skips whitespace starting from the given position using span operations.
    /// </summary>
    private int SkipWhitespaceFrom(int startPosition)
    {
        var bufferSpan = _buffer.AsSpan(startPosition, _bufferUsed - startPosition);
        
        for (int i = 0; i < bufferSpan.Length; i++)
        {
            if (!bufferSpan[i].IsWhitespace())
            {
                return startPosition + i;
            }
        }
        
        return _bufferUsed; // All remaining bytes are whitespace
    }

    /// <summary>
    /// Finds the next opening tag for the specified element within our buffer.
    /// </summary>
    private int FindNextOpeningTag(ReadOnlySpan<byte> elementName)
    {
        var searchSpan = _buffer.AsSpan(_bufferPosition, _bufferUsed - _bufferPosition);
        
        for (int i = 0; i < searchSpan.Length; i++)
        {
            int absolutePosition = _bufferPosition + i;
            int tagIndex = FindOpeningTagAt(absolutePosition, elementName);
            if (tagIndex == absolutePosition) return absolutePosition;
        }
        return -1;
    }

    /// <summary>
    /// Checks if there's an opening tag for the specified element at the given position.
    /// </summary>
    private int FindOpeningTagAt(int position, ReadOnlySpan<byte> elementName)
    {
        if (position >= _bufferUsed || _buffer[position] != LessThan)
            return -1;

        // Check if it's a closing tag
        if (position + 1 < _bufferUsed && _buffer[position + 1] == ForwardSlash)
            return -1;

        // Check if the element name matches using spans
        int nameStart = position + 1;
        if (nameStart + elementName.Length > _bufferUsed)
            return -1; // Not enough data

        var nameSpan = _buffer.AsSpan(nameStart, elementName.Length);
        if (!nameSpan.SequenceEqual(elementName))
            return -1;

        // Check that the character after the name is a valid terminator
        int afterName = nameStart + elementName.Length;
        if (afterName >= _bufferUsed)
            return -1; // Not enough data

        byte terminator = _buffer[afterName];
        if (terminator.IsTagNameTerminal())
            return position;

        return -1;
    }

    /// <summary>
    /// Finds the end of an XML element, handling self-closing tags and nested elements.
    /// Works within the constraints of our buffer.
    /// </summary>
    private int FindElementEnd(int openIndex, ReadOnlySpan<byte> elementName)
    {
        // Skip to end of opening tag
        int cursor = openIndex + 1 + elementName.Length;

        // Parse through the opening tag to see if it's self-closing
        bool insideQuote = false;
        byte quoteChar = 0;

        var bufferSpan = _buffer.AsSpan();
        
        while (cursor < _bufferUsed)
        {
            byte c = bufferSpan[cursor];

            if (!insideQuote && c.IsQuote())
            {
                insideQuote = true;
                quoteChar = c;
            }
            else if (insideQuote && c == quoteChar)
            {
                insideQuote = false;
            }
            else if (!insideQuote)
            {
                if (c == GreaterThan)
                {
                    cursor++;
                    break;
                }
                else if (c == ForwardSlash && cursor + 1 < _bufferUsed && bufferSpan[cursor + 1] == GreaterThan)
                {
                    // Self-closing tag
                    return cursor + 2;
                }
            }
            cursor++;
        }

        if (cursor >= _bufferUsed) return -1; // Need more data

        // Now find the matching closing tag
        int depth = 1;

        while (cursor < _bufferUsed && depth > 0)
        {
            if (bufferSpan[cursor] == LessThan)
            {
                if (cursor + 1 < _bufferUsed && bufferSpan[cursor + 1] == ForwardSlash)
                {
                    // Closing tag - use span comparison
                    int nameStart = cursor + 2;
                    if (nameStart + elementName.Length <= _bufferUsed)
                    {
                        var closingNameSpan = bufferSpan.Slice(nameStart, elementName.Length);
                        if (closingNameSpan.SequenceEqual(elementName))
                        {
                            int afterName = nameStart + elementName.Length;
                            
                            // Skip whitespace
                            while (afterName < _bufferUsed && bufferSpan[afterName].IsWhitespace())
                                afterName++;

                            if (afterName < _bufferUsed && bufferSpan[afterName] == GreaterThan)
                            {
                                depth--;
                                if (depth == 0)
                                    return afterName + 1;
                                cursor = afterName + 1;
                                continue;
                            }
                        }
                    }
                }
                else
                {
                    // Check for nested opening tag
                    int nameStart = cursor + 1;
                    if (nameStart + elementName.Length <= _bufferUsed)
                    {
                        var nestedNameSpan = bufferSpan.Slice(nameStart, elementName.Length);
                        if (nestedNameSpan.SequenceEqual(elementName))
                        {
                            int afterName = nameStart + elementName.Length;
                            if (afterName < _bufferUsed && bufferSpan[afterName].IsTagNameTerminal())
                            {
                                // Check if this nested tag is self-closing
                                if (!IsNestedTagSelfClosing(bufferSpan, afterName, out int tagEnd))
                                {
                                    depth++;
                                }
                                cursor = tagEnd;
                                continue;
                            }
                        }
                    }
                }
            }
            cursor++;
        }

        return depth == 0 ? cursor : -1;
    }

    /// <summary>
    /// Efficiently determines if a nested tag is self-closing using span operations.
    /// </summary>
    private bool IsNestedTagSelfClosing(ReadOnlySpan<byte> bufferSpan, int afterName, out int tagEnd)
    {
        int tagCursor = afterName;
        bool inQuote = false;
        byte qChar = 0;

        while (tagCursor < _bufferUsed)
        {
            byte c = bufferSpan[tagCursor];
            if (!inQuote && c.IsQuote())
            {
                inQuote = true;
                qChar = c;
            }
            else if (inQuote && c == qChar)
            {
                inQuote = false;
            }
            else if (!inQuote)
            {
                if (c == GreaterThan)
                {
                    tagEnd = tagCursor + 1;
                    return false; // Regular tag, not self-closing
                }
                if (c == ForwardSlash && tagCursor + 1 < _bufferUsed && bufferSpan[tagCursor + 1] == GreaterThan)
                {
                    tagEnd = tagCursor + 2;
                    return true; // Self-closing tag
                }
            }
            tagCursor++;
        }

        tagEnd = tagCursor;
        return false; // Incomplete tag, assume not self-closing
    }

    /// <summary>
    /// Disposes the ElementChunker and returns the buffer to the ArrayPool.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }
}
