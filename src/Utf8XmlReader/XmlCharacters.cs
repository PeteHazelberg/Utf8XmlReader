namespace Utf8Xml;

/// <summary>
/// Common UTF-8 constants used throughout the UTF-8 XML parsing library.
/// Centralizes magic numbers and improves code readability.
/// </summary>
internal static class XmlCharacters
{
    #region Structural Characters
    
    /// <summary>Opening angle bracket '&lt;' - starts XML elements, comments, declarations</summary>
    public const byte LessThan = (byte)'<';
    
    /// <summary>Closing angle bracket '&gt;' - ends XML elements, comments, declarations</summary>
    public const byte GreaterThan = (byte)'>';
    
    /// <summary>Forward slash '/' - used in end elements and self-closing elements</summary>
    public const byte ForwardSlash = (byte)'/';
    
    /// <summary>Question mark '?' - used in processing instructions and XML declarations</summary>
    public const byte QuestionMark = (byte)'?';
    
    /// <summary>Exclamation mark '!' - used in comments and DOCTYPE declarations</summary>
    public const byte ExclamationMark = (byte)'!';
    
    /// <summary>Equals sign '=' - used in attribute assignments</summary>
    public const byte EqualsSign = (byte)'=';
    
    /// <summary>Colon ':' - used in namespace prefixes</summary>
    public const byte Colon = (byte)':';

    #endregion

    #region Quote Characters
    
    /// <summary>Double quote '"' - used to delimit attribute values</summary>
    public const byte DoubleQuote = (byte)'"';
    
    /// <summary>Single quote "'" - used to delimit attribute values</summary>
    public const byte SingleQuote = (byte)'\'';

    #endregion

    #region Whitespace Characters
    
    /// <summary>Space character ' '</summary>
    public const byte Space = (byte)' ';
    
    /// <summary>Tab character '\t'</summary>
    public const byte Tab = (byte)'\t';
    
    /// <summary>Line feed '\n'</summary>
    public const byte LineFeed = (byte)'\n';
    
    /// <summary>Carriage return '\r'</summary>
    public const byte CarriageReturn = (byte)'\r';

    #endregion

    #region Numeric Characters
    
    /// <summary>Digit zero '0'</summary>
    public const byte DigitZero = (byte)'0';
    
    /// <summary>Digit nine '9'</summary>
    public const byte DigitNine = (byte)'9';
    
    /// <summary>Minus sign '-' - used for negative numbers</summary>
    public const byte MinusSign = (byte)'-';

    #endregion

    #region Common Letter Sequences
    
    /// <summary>Lowercase 'x' - used in XML declaration detection</summary>
    public const byte LowercaseX = (byte)'x';
    
    /// <summary>Lowercase 'm' - used in XML declaration detection</summary>
    public const byte LowercaseM = (byte)'m';
    
    /// <summary>Lowercase 'l' - used in XML declaration detection</summary>
    public const byte LowercaseL = (byte)'l';

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines if a byte represents an XML whitespace character.
    /// XML whitespace includes: space (0x20), tab (0x09), line feed (0x0A), carriage return (0x0D).
    /// </summary>
    /// <param name="b">The byte to check</param>
    /// <returns>true if the byte is XML whitespace; otherwise, false</returns>
    public static bool IsWhitespace(this byte b) => 
        b <= Space && (b == Space || b == Tab || b == LineFeed || b == CarriageReturn);

    /// <summary>
    /// Determines if a byte can terminate an XML element name.
    /// Valid terminators are: '&gt;', '/', or any whitespace character.
    /// </summary>
    /// <param name="b">The byte to check</param>
    /// <returns>true if the byte can terminate an element name; otherwise, false</returns>
    public static bool IsTagNameTerminal(this byte b) => 
        b == GreaterThan || b == ForwardSlash || b.IsWhitespace();

    /// <summary>
    /// Determines if a byte represents a numeric digit (0-9).
    /// </summary>
    /// <param name="b">The byte to check</param>
    /// <returns>true if the byte is a digit; otherwise, false</returns>
    public static bool IsDigit(this byte b) => 
        b is >= DigitZero and <= DigitNine;

    /// <summary>
    /// Determines if a byte represents a quote character (single or double).
    /// </summary>
    /// <param name="b">The byte to check</param>
    /// <returns>true if the byte is a quote character; otherwise, false</returns>
    public static bool IsQuote(this byte b) => 
        b is DoubleQuote or SingleQuote;

    #endregion
}
