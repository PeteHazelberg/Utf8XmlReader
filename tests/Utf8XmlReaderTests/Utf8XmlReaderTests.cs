using FluentAssertions;
using System.Text;
using System.Xml;

namespace Utf8Xml;

public class Utf8XmlReaderTests
{
    [Fact]
    public void SimpleRootElementWithAttributeAndContent()
    {
        var reader = Utf8XmlReader.Create("""<string xmlns="http://example.com">text</string>"""u8);
        reader.Read().Should().BeTrue();
        Assert.Equal(XmlNodeType.Element, reader.NodeType);
        Assert.Equal("string"u8, reader.LocalName);

        reader.GetAttribute("xmlns"u8).SequenceEqual("http://example.com"u8).Should().BeTrue();
        
        reader.ReadContentAsBytes().SequenceEqual("text"u8).Should().BeTrue();
        reader.NodeType.Should().Be(XmlNodeType.EndElement);
        reader.LocalName.SequenceEqual("string"u8).Should().BeTrue();

        reader.Read().Should().BeFalse();
    }

    [Theory]
    [InlineData(ReaderType.Utf16)]
    [InlineData(ReaderType.Utf8)]
    public void CompareNestedElementsBehaviorWith_System_Xml_XmlReader(ReaderType readerType)
    {
        var xml = """
                  <?xml version="1.0" encoding="utf-8"?>
                  <root>
                    <container type="folder" />
                    <container>
                      <leaf>content</leaf>
                      <leaf>1234</leaf>
                      <leaf>skip me</leaf>
                      <leaf />
                    </container>
                  </root>
                  """u8;

        // C# helps enforce that ref struct types are stack only and never "accidentally" boxed and put on the heap.
        // Therefore, we cannot create a factory method that returns an ITestableXmlReader implemented by a ref struct type.
        // So, we are stuck with two distinct variables and code paths here.
        // We CAN use generics with constraints to pass ref struct types generically to a method to enable some shared code.
        if (readerType == ReaderType.Utf8)
        {
            var utf8 = new Utf8Testable(xml);
            Tests(ref utf8);
            return;
        }

        var utf16 = new Utf16Testable(xml);
        Tests(ref utf16);
        return;


        void Tests<T>(ref T reader) where T : struct, ITestableXmlReader, allows ref struct
        {
            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.XmlDeclaration);

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("root"u8).Should().BeTrue();
            reader.Depth.Should().Be(0);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("container"u8).Should().BeTrue();
            reader.Depth.Should().Be(1, "this is an empty container element."); // empty
            reader.IsEmptyElement.Should().BeTrue();
            reader.GetAttribute("type"u8).SequenceEqual("folder"u8).Should().BeTrue("this container has no type attribute.");

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("container"u8).Should().BeTrue();
            reader.Depth.Should().Be(1);
            reader.GetAttribute("type"u8).Length.Should().Be(0);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("leaf"u8).Should().BeTrue();
            reader.Depth.Should().Be(2);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Text);
            reader.LocalName.Length.Should().Be(0);
            reader.Depth.Should().Be(3);
            reader.IsEmptyElement.Should().BeFalse();

            reader.ReadContentAsBytes().SequenceEqual("content"u8).Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.EndElement);
            reader.LocalName.SequenceEqual("leaf"u8).Should().BeTrue();
            reader.Depth.Should().Be(2);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("leaf"u8).Should().BeTrue();
            reader.Depth.Should().Be(2);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Text);
            reader.LocalName.Length.Should().Be(0);
            reader.Depth.Should().Be(3);
            reader.IsEmptyElement.Should().BeFalse();

            reader.ReadContentAsInt().Should().Be(1234);
            reader.NodeType.Should().Be(XmlNodeType.EndElement);
            reader.LocalName.SequenceEqual("leaf"u8).Should().BeTrue();
            reader.Depth.Should().Be(2);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("leaf"u8).Should().BeTrue();
            reader.Depth.Should().Be(2);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Skip(); // skip "skip me" leaf

            reader.NodeType.Should().Be(XmlNodeType.Element);
            reader.LocalName.SequenceEqual("leaf"u8).Should().BeTrue();
            reader.Depth.Should().Be(2);
            reader.IsEmptyElement.Should().BeTrue();

            reader.Skip(); // skip the empty leaf

            reader.NodeType.Should().Be(XmlNodeType.EndElement);
            reader.LocalName.SequenceEqual("container"u8).Should().BeTrue();
            reader.Depth.Should().Be(1);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeTrue();
            reader.NodeType.Should().Be(XmlNodeType.EndElement);
            reader.LocalName.SequenceEqual("root"u8).Should().BeTrue();
            reader.Depth.Should().Be(0);
            reader.IsEmptyElement.Should().BeFalse();

            reader.Read().Should().BeFalse();
        }
    }


    [Fact]
    public void ReadEmptyDocumentWithXmlDeclaration()
    {
        var xml = """<?xml version="1.0" encoding="utf-8"?>"""u8;
        var reader = Utf8XmlReader.Create(xml);
        reader.Read().Should().BeTrue();
        reader.NodeType.Should().Be(XmlNodeType.XmlDeclaration);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void ReadEmptyDocument()
    {
        var xml = ""u8;
        var reader = Utf8XmlReader.Create(xml);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void ReadDocumentWithOnlyWhitespace()
    {
        var xml = """    
        
        
        """u8;
        var reader = Utf8XmlReader.Create(xml);
        reader.Read().Should().BeFalse();
    }

    [Theory]
    [InlineData(ReaderType.Utf16)]
    [InlineData(ReaderType.Utf8)]
    public void Read_MalformedXml_ThrowsException(ReaderType readerType)
    {
        var xml = """<root><unclosed></root>"""u8;
        if (readerType == ReaderType.Utf8)
        {
            var utf8 = new Utf8Testable(xml);
            Tests(ref utf8);
            return;
        }

        var utf16 = new Utf16Testable(xml);
        Tests(ref utf16);
        return;


        void Tests<T>(ref T reader) where T : struct, ITestableXmlReader, allows ref struct
        {
            try
            {
                while (reader.Read())
                {
                    // just read through the document
                }
            }
            catch (XmlException e)
            {
                e.Message.Should().ContainAll("unclosed", "not match");
                return;
            }

            Assert.Fail("Expected XmlException was not thrown.");
        }
    }

}

public interface ITestableXmlReader
{
    bool Read();
    XmlNodeType NodeType { get; }
    ReadOnlySpan<byte> LocalName { get; }
    int Depth { get; }
    bool IsEmptyElement { get; }
    ReadOnlySpan<byte> ReadContentAsBytes();
    int ReadContentAsInt();
    ReadOnlySpan<byte> GetAttribute(ReadOnlySpan<byte> attrName);
    void Skip();
}


public readonly ref struct Utf16Testable(ReadOnlySpan<byte> xml) : ITestableXmlReader
{
    private readonly XmlReader _reader = XmlReader.Create(new MemoryStream(xml.ToArray()));

    public bool Read()
    {
        var readSuccess = _reader.Read();
        while (_reader.NodeType == XmlNodeType.Whitespace)
            readSuccess = _reader.Read();

        return readSuccess;
    }

    public XmlNodeType NodeType => _reader.NodeType;
    public ReadOnlySpan<byte> LocalName => Encoding.UTF8.GetBytes(_reader.LocalName);
    public int Depth => _reader.Depth;
    public bool IsEmptyElement => _reader.IsEmptyElement;
    public ReadOnlySpan<byte> ReadContentAsBytes() => Encoding.UTF8.GetBytes(_reader.ReadContentAsString());

    public int ReadContentAsInt() => _reader.ReadContentAsInt();

    public ReadOnlySpan<byte> GetAttribute(ReadOnlySpan<byte> attrName) => 
        Encoding.UTF8.GetBytes(_reader.GetAttribute(Encoding.UTF8.GetString(attrName)) ?? "");

    public void Skip()
    {
        _reader.Skip();
        while (_reader.NodeType == XmlNodeType.Whitespace)
            _reader.Read();
    }
}

public ref struct Utf8Testable : ITestableXmlReader
{
    private Utf8XmlReader _reader;
    private readonly ReadOnlySpan<byte> _xml;

    public Utf8Testable(ReadOnlySpan<byte> xml)
    {
        _xml = xml;
        _reader = Utf8XmlReader.Create(_xml);
    }

    public bool Read() => _reader.Read();
    public XmlNodeType NodeType => _reader.NodeType;
    public ReadOnlySpan<byte> LocalName => _reader.LocalName;
    public int Depth => _reader.Depth;
    public bool IsEmptyElement => _reader.IsEmptyElement;
    public ReadOnlySpan<byte> ReadContentAsBytes() => _reader.ReadContentAsBytes();
    public int ReadContentAsInt() => _reader.ReadContentAsInt();
    public ReadOnlySpan<byte> GetAttribute(ReadOnlySpan<byte> attrName) => _reader.GetAttribute(attrName);
    public void Skip() => _reader.Skip();
}

public enum ReaderType
{
    Unknown = 0,
    Utf8,
    Utf16
}
