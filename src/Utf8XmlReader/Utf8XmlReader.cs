using System.Text;
using System.Xml;
using static Utf8Xml.XmlCharacters;

namespace Utf8Xml;

/// <summary>
/// A stack-only, non-allocating, non-namespace-aware XML reader for UTF-8 XML data.
/// Designed for high-performance parsing with minimal memory allocations.
/// </summary>
public ref struct Utf8XmlReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _pos;
    private int _internalDepth;
    private int _depth; // The depth of the current element for external reporting
    private XmlNodeType _nodeType;
    private ReadOnlySpan<byte> _localName;
    private bool _isEmptyElement;
    private int _contentStart;
    private int _contentEnd;
    private AttributeEnumerator _attributeEnumerator;
    private ElementStack _elementStack;

    public static Utf8XmlReader Create(ReadOnlySpan<byte> utf8Xml)
    {
        return new Utf8XmlReader(utf8Xml);
    }

    private Utf8XmlReader(ReadOnlySpan<byte> utf8Xml)
    {
        _buffer = utf8Xml;
        _pos = 0;
        _internalDepth = -1; // Start at -1 so first element is at depth 0
        _depth = 0;
        _nodeType = XmlNodeType.None;
        _localName = default;
        _isEmptyElement = false;
        _contentStart = -1;
        _contentEnd = -1;
        _attributeEnumerator = default;
        _elementStack = new ElementStack();
    }

    public ReadOnlySpan<byte> LocalName => _localName;
    public bool IsEmptyElement => _isEmptyElement;
    public XmlNodeType NodeType => _nodeType;
    public string LocalNameAsString => Encoding.UTF8.GetString(_localName);

    /// <summary> How deep into the XML element hierarchy the reader currently is. </summary>
    public int Depth => _depth;

    /// <summary>
    /// Advances the reader to the next node in the XML stream.
    /// </summary>
    /// <returns>true if the next node was read successfully; false if there are no more nodes to read.</returns>
    public bool Read()
    {
        // Reset content markers
        _contentStart = -1;
        _contentEnd = -1;
        _attributeEnumerator = default;
        _isEmptyElement = false;
        _localName = default;

        SkipWhitespace();
        if (_pos >= _buffer.Length)
            return false;

        var currentByte = _buffer[_pos];
        if (currentByte == LessThan)
        {
            // Check the next byte to determine element type
            if (_pos + 1 >= _buffer.Length)
                return false;
                
            var nextByte = _buffer[_pos + 1];
            
            // Processing instruction or XML declaration
            if (nextByte == QuestionMark)
            {
                return ProcessProcessingInstruction();
            }
            
            // End element
            if (nextByte == ForwardSlash)
            {
                return ProcessEndElement();
            }
            
            // Comment or declaration
            if (nextByte == ExclamationMark)
            {
                return ProcessCommentOrDeclaration();
            }
            
            // Start element
            return ProcessStartElement();
        }

        // Text content
        return ProcessTextContent();
    }

    /// <summary>
    /// Extracts the attribute value for the given attribute name from the current XML element.
    /// </summary>
    /// <param name="name">XML attribute name</param>
    /// <returns>An empty span if attribute not found, otherwise the attribute value span</returns>
    public ReadOnlySpan<byte> GetAttribute(ReadOnlySpan<byte> name)
    {
        if (_nodeType != XmlNodeType.Element) return default;
        return _attributeEnumerator.GetAttribute(name);
    }

    /// <summary>
    /// Reads the text content of the current element as UTF-8 bytes.
    /// </summary>
    /// <returns>The content as UTF-8 bytes, or empty span if no content.</returns>
    public ReadOnlySpan<byte> ReadContentAsBytes()
    {
        if (_nodeType == XmlNodeType.Element)
        {
            // If we're on an element, we need to advance to read its content
            if (_isEmptyElement)
                return default; // Empty elements have no content

            // Read the next node to get to the content
            if (!Read())
                return default;

            // If we hit another element or end element, there's no text content
            if (_nodeType != XmlNodeType.Text)
                return default;

            // Return the content and advance to the end element
            var content = _buffer.Slice(_contentStart, _contentEnd - _contentStart);

            // Advance to the end element
            Read();

            return content;
        }

        if (_nodeType != XmlNodeType.Text)
            return default;

        var textContent = _buffer.Slice(_contentStart, _contentEnd - _contentStart);

        // Advance to the end element after reading text content
        Read();

        return textContent;
    }

    /// <summary>
    /// Advances the reader to the next occurrence of the specified element.
    /// </summary>
    /// <param name="name">The local name of the element to find.</param>
    /// <returns>true if the element was found; otherwise, false.</returns>
    public bool ReadToFollowing(ReadOnlySpan<byte> name)
    {
        if (name.Length == 0)
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        // Optimize by checking first byte as a fast filter
        var firstByte = name[0];
        while (Read())
        {
            if (NodeType == XmlNodeType.Element && 
                LocalName.Length == name.Length &&
                LocalName.Length > 0 &&
                LocalName[0] == firstByte &&
                LocalName.SequenceEqual(name))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Advances past the children elements of this element.
    /// </summary>
    public void Skip()
    {
        if (NodeType != XmlNodeType.Element)
            return;
        var startDepth = Depth;
        while (Read())
        {
            if (NodeType == XmlNodeType.Element && Depth == startDepth)
                break; // we're at the next peer element
            if (NodeType == XmlNodeType.EndElement && Depth < startDepth)
                break; // we've exited the parent element
        }
    }

    /// <summary>
    /// Reads the text content of the current element as a string.
    /// </summary>
    public string ReadContentAsString() => Encoding.UTF8.GetString(ReadContentAsBytes());

    /// <summary>
    /// Reads the text content of the current element and parses it as an integer.
    /// </summary>
    public int ReadContentAsInt()
    {
        var content = ReadContentAsBytes();
        if (content.Length == 0)
            throw new FormatException("Content is empty.");
            
        // Optimized integer parsing - avoid string allocation
        int result = 0;
        bool negative = false;
        int i = 0;
        
        // Handle negative sign
        if (content.Length > 0 && content[0] == MinusSign)
        {
            negative = true;
            i = 1;
        }
        
        // Parse digits
        for (; i < content.Length; i++)
        {
            var b = content[i];
            if (!b.IsDigit())
                throw new FormatException($"Invalid character '{(char)b}' in integer content.");
            result = result * 10 + (b - DigitZero);
        }
        
        return negative ? -result : result;
    }

    public override string ToString()
    {
        return $"{NodeType}, {LocalNameAsString}";
    }

    /// <summary>
    /// Processes XML processing instructions and declarations.
    /// </summary>
    private bool ProcessProcessingInstruction()
    {
        int declStart = _pos + 2;
        // Fast check for XML declaration using direct byte comparison
        var buffer = _buffer;
        if (buffer.Length - declStart >= 3 && 
            buffer[declStart] == LowercaseX && 
            buffer[declStart + 1] == LowercaseM && 
            buffer[declStart + 2] == LowercaseL)
        {
            // Advance until we find '?>'
            _pos += 2; // skip '<?'
            while (_pos + 1 < buffer.Length && !(buffer[_pos] == QuestionMark && buffer[_pos + 1] == GreaterThan))
                _pos++;
            if (_pos + 1 < buffer.Length)
                _pos += 2; // skip '?>'
            _nodeType = XmlNodeType.XmlDeclaration;
            _depth = 0; // declaration does not affect element depth
            return true;
        }
        else
        {
            // Non-xml processing instruction: skip and read next meaningful node
            _pos += 2; // skip '<?'
            while (_pos + 1 < buffer.Length && !(buffer[_pos] == QuestionMark && buffer[_pos + 1] == GreaterThan))
                _pos++;
            if (_pos + 1 < buffer.Length)
                _pos += 2; // skip '?>'
            return Read();
        }
    }

    /// <summary>
    /// Processes XML end elements (e.g., &lt;/element&gt;).
    /// </summary>
    private bool ProcessEndElement()
    {
        _pos += 2; // skip '</'
        var nameStart = _pos;
        var buffer = _buffer;
        while (_pos < buffer.Length && buffer[_pos] != GreaterThan)
            _pos++;
        _localName = buffer.Slice(nameStart, _pos - nameStart);
        _pos++; // skip '>'
        
        // Validate that the end tag matches the expected start tag
        if (!_elementStack.Pop(out var expectedName))
        {
            throw new XmlException($"Unexpected end element '</{Encoding.UTF8.GetString(_localName)}>'.");
        }
        
        if (!_localName.SequenceEqual(expectedName))
        {
            throw new XmlException(
                $"The '{Encoding.UTF8.GetString(expectedName)}' start tag on line 1 position {nameStart} " +
                $"does not match the end tag of '{Encoding.UTF8.GetString(_localName)}'. Line 1, position {_pos}.");
        }
        
        // Confirm the pop after successful validation
        _elementStack.PopConfirmed();
        
        _nodeType = XmlNodeType.EndElement;
        _depth = _internalDepth; // End element is at the current depth
        _internalDepth--; // Decrease depth after processing end element
        return true;
    }

    /// <summary>
    /// Processes XML comments and other declarations.
    /// </summary>
    private bool ProcessCommentOrDeclaration()
    {
        var buffer = _buffer;
        // Fast check for comment using direct byte comparison
        if (_pos + 3 < buffer.Length && 
            buffer[_pos + 2] == MinusSign && 
            buffer[_pos + 3] == MinusSign)
        {
            _pos += 4; // skip '<!--'
            while (_pos + 2 < buffer.Length && 
                   !(buffer[_pos] == MinusSign && buffer[_pos + 1] == MinusSign && buffer[_pos + 2] == GreaterThan))
                _pos++;
            _pos += 3; // skip '-->'
            return Read();
        }
        // Skip other declarations (e.g., <!DOCTYPE ...>)
        while (_pos < buffer.Length && buffer[_pos] != GreaterThan)
            _pos++;
        _pos++;
        return Read();
    }

    /// <summary>
    /// Processes XML start elements, including attribute parsing and self-closing detection.
    /// </summary>
    private bool ProcessStartElement()
    {
        _pos++; // skip '<'
        var nameStart = _pos;
        var buffer = _buffer;
        
        // Optimized element name parsing
        while (_pos < buffer.Length)
        {
            var b = buffer[_pos];
            if (b <= 32 || b == ForwardSlash || b == GreaterThan) break;
            _pos++;
        }
        _localName = buffer.Slice(nameStart, _pos - nameStart);

        // Skip whitespace after element name
        SkipWhitespace();

        // Parse attributes and find end of start tag
        _attributeEnumerator = new AttributeEnumerator(buffer, _pos);

        var empty = false;
        // Optimized attribute scanning
        while (_pos < buffer.Length)
        {
            var b = buffer[_pos];
            if (b == GreaterThan) break; // Found end of start tag

            if (b == ForwardSlash)
            {
                if (_pos + 1 < buffer.Length && buffer[_pos + 1] == GreaterThan)
                {
                    empty = true;
                    _pos++; // skip '/'
                    break;
                }
                _pos++; // skip '/' that's not part of self-closing tag
            }
            else if (b.IsQuote())
            {
                // Skip quoted attribute value efficiently
                byte quote = b;
                _pos++; // skip opening quote
                while (_pos < buffer.Length && buffer[_pos] != quote)
                    _pos++;
                if (_pos < buffer.Length) _pos++; // skip closing quote
            }
            else
            {
                _pos++;
            }
        }

        if (_pos < buffer.Length && buffer[_pos] == GreaterThan) _pos++; // skip '>'

        // All elements increment depth as they are processed  
        _internalDepth++;
        _depth = _internalDepth; // Store the depth for this element

        _isEmptyElement = empty;
        _nodeType = XmlNodeType.Element;

        // Push element name onto stack for validation (only if not self-closing)
        if (!empty)
        {
            _elementStack.Push(_localName);
        }
        else
        {
            // For empty elements, they don't create a new container level for future siblings
            _internalDepth--;
        }

        return true;
    }

    /// <summary>
    /// Processes text content between XML elements.
    /// </summary>
    private bool ProcessTextContent()
    {
        _contentStart = _pos;
        var buffer = _buffer;
        while (_pos < buffer.Length && buffer[_pos] != LessThan)
            _pos++;
        _contentEnd = _pos;
        _nodeType = XmlNodeType.Text;
        _depth = _internalDepth + 1; // Text is one level deeper than current depth
        return true;
    }

    /// <summary>
    /// Efficiently skips XML whitespace characters.
    /// </summary>
    private void SkipWhitespace()
    {
        var buffer = _buffer;
        var pos = _pos;
        var length = buffer.Length;
        
        // Use vectorized approach for better performance
        while (pos < length)
        {
            var b = buffer[pos];
            if (b > 32) break; // Fast path: anything > space is not whitespace
            if (!b.IsWhitespace()) break;
            pos++;
        }
        
        _pos = pos;
    }

    /// <summary>
    /// Efficiently parses XML attributes without memory allocations.
    /// </summary>
    private readonly ref struct AttributeEnumerator(ReadOnlySpan<byte> buffer, int pos)
    {
        private readonly ReadOnlySpan<byte> _buffer = buffer;
        private readonly int _limit = buffer.Length;

        /// <summary>
        /// Gets the value of the specified attribute, supporting both full names and local names.
        /// </summary>
        /// <param name="requestedName">The attribute name to search for</param>
        /// <returns>The attribute value as UTF-8 bytes, or empty span if not found</returns>
        public ReadOnlySpan<byte> GetAttribute(ReadOnlySpan<byte> requestedName)
        {
            int p = pos;
            var buffer = _buffer;
            var limit = _limit;
            
            while (p < limit)
            {
                // Skip whitespace efficiently
                while (p < limit && buffer[p].IsWhitespace()) p++;
                if (p >= limit || buffer[p] == GreaterThan || buffer[p] == ForwardSlash) break;
                
                // Parse attribute name
                int nameStart = p;
                while (p < limit && buffer[p] != EqualsSign && buffer[p] > 32) p++;
                int nameLen = p - nameStart;
                var attrName = buffer.Slice(nameStart, nameLen);
                
                // Skip to equals sign
                while (p < limit && buffer[p] != EqualsSign) p++;
                p++; // skip '='
                
                // Skip whitespace
                while (p < limit && buffer[p].IsWhitespace()) p++;
                if (p >= limit) break;
                
                // Parse attribute value (quoted)
                if (!buffer[p].IsQuote()) break;
                byte quote = buffer[p++];
                int valueStart = p;
                while (p < limit && buffer[p] != quote) p++;
                int valueLen = p - valueStart;
                var attrValue = buffer.Slice(valueStart, valueLen);
                p++; // skip closing quote
                
                // Check full name match first
                if (attrName.SequenceEqual(requestedName))
                    return attrValue;
                    
                // Check local name after colon
                var colonIndex = attrName.IndexOf(Colon);
                if (colonIndex > -1 && colonIndex + 1 < attrName.Length)
                {
                    var localAttrName = attrName.Slice(colonIndex + 1);
                    if (localAttrName.SequenceEqual(requestedName))
                        return attrValue;
                }
            }
            return default; // Return empty span if not found
        }
    }

    /// <summary>
    /// Stack for tracking element names to validate proper nesting.
    /// Uses inline arrays (C# 12+) for stack-allocated storage with zero heap allocations.
    /// </summary>
    private struct ElementStack
    {
        private const int MaxDepth = 64;
        private const int MaxNameLength = 256;
        
        private ElementNameBuffer _buffer;
        private int _count;

        [System.Runtime.CompilerServices.InlineArray(MaxDepth * MaxNameLength)]
        private struct ElementNameBuffer
        {
            private byte _element0;
        }
        
        private struct ElementInfo
        {
            public int Offset;
            public int Length;
        }

        [System.Runtime.CompilerServices.InlineArray(MaxDepth)]
        private struct ElementInfoArray
        {
            private ElementInfo _element0;
        }

        private ElementInfoArray _infos;
        private int _bufferPos;

        public void Push(ReadOnlySpan<byte> elementName)
        {
            if (_count >= MaxDepth)
                throw new XmlException($"Maximum element depth of {MaxDepth} exceeded.");
            
            if (elementName.Length > MaxNameLength)
                throw new XmlException($"Element name exceeds maximum length of {MaxNameLength}.");
            
            if (_bufferPos + elementName.Length > MaxDepth * MaxNameLength)
                throw new XmlException("Element stack buffer overflow.");
            
            _infos[_count] = new ElementInfo 
            { 
                Offset = _bufferPos, 
                Length = elementName.Length 
            };
            
            // Copy element name into buffer
            var targetSpan = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref _buffer[_bufferPos], 
                elementName.Length);
            elementName.CopyTo(targetSpan);
            
            _bufferPos += elementName.Length;
            _count++;
        }

        public readonly bool Pop(out ReadOnlySpan<byte> elementName)
        {
            if (_count == 0)
            {
                elementName = default;
                return false;
            }
            
            var info = _infos[_count - 1];
            elementName = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.AsRef(in _buffer[info.Offset]), 
                info.Length);
            
            return true;
        }

        public void PopConfirmed()
        {
            if (_count > 0)
            {
                _count--;
                if (_count > 0)
                    _bufferPos = _infos[_count - 1].Offset + _infos[_count - 1].Length;
                else
                    _bufferPos = 0;
            }
        }
    }
}
