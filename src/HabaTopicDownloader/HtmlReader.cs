using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace NiL.HttpUtils
{
    public class HtmlReader : XmlReader
    {
        private static readonly HashSet<string> _ForceCloseTags = new HashSet<string>
        {
            "br",
            "meta"
        };

        private static readonly HashSet<string> _AutoCloseTags = new HashSet<string>
        {
            //"meta",
            "br",
            "link",
            "img"
        };

        private static readonly HashSet<string> _AllowUnclosed = new HashSet<string>
        {
            "meta",
            "br",
            "link",
            "img",
            "span",
            "hr"
        };

        private sealed class Node
        {
            public string Name { get; private set; }
            public List<KeyValuePair<string, string>> Attributes { get; private set; }
            public List<Node> Childs { get; private set; }

            public Node(string name)
            {
                Name = name;
                Attributes = new List<KeyValuePair<string, string>>();
                Childs = new List<Node>();
            }
        }

        private delegate XmlElement XmlElementCtorDelegate(string prefix, string localName, string namespaceURI, XmlDocument doc);

        private static readonly XmlElementCtorDelegate _XmlElementCtor = null;

        private XmlNode _root;
        private XmlNode _currentNode;
        private int _depth;
        private ReadState _readState;

        public XmlDocument Document
        {
            get
            {
                return _root.OwnerDocument ?? (_root as XmlDocument);
            }
        }

        static HtmlReader()
        {
            var prefixParameter = Expression.Parameter(typeof(string), "prefix");
            var localNameParameter = Expression.Parameter(typeof(string), "localName");
            var namespaceUriParameter = Expression.Parameter(typeof(string), "namesapceURI");
            var docParameter = Expression.Parameter(typeof(XmlDocument), "doc");

            var ctor = typeof(XmlElement).GetTypeInfo().GetConstructors(
                BindingFlags.NonPublic
                | BindingFlags.Public
                | BindingFlags.Instance);

            var expression =
            Expression.Lambda<XmlElementCtorDelegate>(Expression.New(
            ctor[1],
            prefixParameter, localNameParameter, namespaceUriParameter, docParameter),
            prefixParameter, localNameParameter, namespaceUriParameter, docParameter);

            _XmlElementCtor = expression.Compile();
        }

        private HtmlReader()
        {
        }

        public new static HtmlReader Create(Stream inputStream)
        {
            var html = new StreamReader(inputStream).ReadToEnd();
            var depth = 0;

            return new HtmlReader { _root = parseDoc(html, ref depth), _depth = depth };
        }

        private static XmlDocument parseDoc(string source, ref int depth)
        {
            var index = 0;
            var doc = new XmlDocument();

            XmlNode node;
            List<string> openedTags = new List<string>();
            do
            {
                node = parseNode(source, ref index, doc, 0, ref depth, openedTags);
                if (node != null && node.NodeType != XmlNodeType.Text)
                    doc.AppendChild(node);
            }
            while (node != null);

            return doc;
        }

        private static XmlNode parseNode(string source, ref int index, XmlDocument doc, int depth, ref int maxDepth, List<string> openedTags)
        {
            if (maxDepth < depth)
                maxDepth = depth;

            // skipSpaces(source, ref index);

            if (index >= source.Length)
                return null;

            var value = "";

            if (source[index] == '<')
            {
                index++;
                var start = index;

                if (source[index] == '/')
                {
                    index--;
                    return null;
                }
                else if (source[index] == '!')
                {
                    index++;
                    if (validateName(source, ref index))
                    {
                        var name = source.Substring(start, index - start);

                        if (name == "!DOCTYPE" && depth == 0)
                        {
                            index++;
                            skipSpaces(source, ref index);

                            start = index;

                            while (source[index] != '>')
                                index++;

                            value = source.Substring(start, index - start);

                            index++;

                            var result = doc.CreateComment("!DOCTYPE " + value);
                            return result;
                        }
                    }
                    else if (source[index] == '-' && source[index + 1] == '-')
                    {
                        index += 2;
                        start = index;

                        while (source[index + 1] != '-' || source[index + 2] != '-' || source[index + 3] != '>')
                            index++;

                        index += 3;

                        if (source[index] != '>')
                            throw new InvalidOperationException();

                        index++;

                        return doc.CreateComment(source.Substring(start, index - start));
                    }

                    throw new InvalidOperationException();
                }
                else
                {
                    validateName(source, ref index);
                    var name = source.Substring(start, index - start).ToLowerInvariant();

                    if (!IsName(name))
                        throw new InvalidOperationException(":" + index);

                    openedTags.Add(name);
                    skipSpaces(source, ref index);
                    start = index;
                    var result = doc.CreateElement(name);

                    while (validateName(source, ref index))
                    {
                        var attributeName = source.Substring(start, index - start);

                        skipSpaces(source, ref index);

                        if (source[index] == '=')
                        {
                            index++;
                            skipSpaces(source, ref index);

                            start = index;
                            if (!validateString(source, ref index))
                                throw new InvalidOperationException(":" + attributeName);

                            if (source[start] == '\'' || source[start] == '"')
                            {
                                start++;
                                index--;
                            }

                            value = source.Substring(start, index - start);

                            if (source[start - 1] == '\'' || source[start - 1] == '"')
                                index++;

                            var attribute = doc.CreateAttribute(attributeName);
                            attribute.Value = value;

                            result.Attributes.Append(attribute);
                        }

                        skipSpaces(source, ref index);
                        start = index;
                    }

                    if (string.CompareOrdinal(name, "br") == 0)
                    {
                        result.InnerText = Environment.NewLine;
                    }

                    if (string.CompareOrdinal(name, "td") == 0)
                    {
                        //result.AppendChild(doc.CreateTextNode(" "));
                    }

                    while (source[index] != '>')
                        index++;

                    if (source[index - 1] == '/')
                    {
                        index++;
                        openedTags.RemoveAt(openedTags.Count - 1);
                        return result;
                    }

                    index++;

                    if (_ForceCloseTags.Contains(name))
                    {
                        openedTags.RemoveAt(openedTags.Count - 1);
                        return result;
                    }

                    if (string.CompareOrdinal(name, "script") == 0)
                    {
                        start = index;
                        index = source.IndexOf("</script", index, StringComparison.OrdinalIgnoreCase);

                        result.InnerText = source.Substring(start, index - start);

                        index += "</script".Length;
                        skipSpaces(source, ref index);

                        if (source[index] == '>')
                            index++;
                        else
                            throw new InvalidOperationException(":" + index);
                    }
                    else
                    {
                        for (var repeat = true; repeat;)
                        {
                            repeat = false;

                            if (!_AutoCloseTags.Contains(name))
                            {
                                XmlNode node;
                                do
                                {
                                    node = parseNode(source, ref index, doc, depth + 1, ref maxDepth, openedTags);
                                    if (node != null)
                                    {
                                        result.AppendChild(node);
                                    }
                                }
                                while (node != null);
                            }
                            else
                            {
                                skipSpaces(source, ref index);
                            }

                            if (source[index] == '<' && source[index + 1] == '/')
                            {
                                if (source.IndexOf(name, index + 2, name.Length) != index + 2
                                    || char.IsLetterOrDigit(source[index + 2 + name.Length]))
                                {
                                    index += 2;
                                    start = index;
                                    validateName(source, ref index);
                                    var closeTagName = source.Substring(start, index - start);
                                    if (openedTags.LastIndexOf(closeTagName) == -1)
                                    {
                                        while (source[index] != '>')
                                            index++;
                                        index++;
                                        repeat = true;
                                        continue;
                                    }
                                    else
                                    {
                                        index = start - 2;
                                    }
                                }
                                else
                                {
                                    index += "</".Length + name.Length;
                                    skipSpaces(source, ref index);
                                    if (source[index] == '>')
                                        index++;
                                    else
                                        throw new InvalidOperationException(":" + index);
                                }
                            }
                        }
                    }

                    if ((name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]))
                        || string.CompareOrdinal(name, "li") == 0
                        || string.CompareOrdinal(name, "tr") == 0
                        || string.CompareOrdinal(name, "th") == 0
                        || string.CompareOrdinal(name, "p") == 0)
                    {
                        result.AppendChild(doc.CreateTextNode(Environment.NewLine));
                    }
                    else if (string.CompareOrdinal(name, "td") == 0)
                    {
                        result.AppendChild(doc.CreateTextNode(" "));
                    }

                    if (openedTags[openedTags.Count - 1] == name)
                        openedTags.RemoveAt(openedTags.Count - 1);

                    return result;
                }
            }
            else
            {
                var start = index;

                while (source[index] != '<' || (source[index + 1] != '!'
                                                && source[index + 1] != '/'
                                                && !char.IsLetter(source[index + 1])))
                {
                    index++;

                    if (index >= source.Length)
                        break;
                }

                return doc.CreateTextNode(source.Substring(start, index - start));
            }
        }

        [DebuggerStepThrough]
        private static void skipSpaces(string source, ref int index)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
                index++;
        }

        [DebuggerStepThrough]
        private static bool validateName(string code)
        {
            return validateName(code, 0);
        }

        [DebuggerStepThrough]
        private static bool validateName(string code, int index)
        {
            return validateName(code, ref index);
        }

        [DebuggerStepThrough]
        private static bool validateName(string code, ref int index)
        {
            if (string.IsNullOrEmpty(code))
                return false;

            int j = index;
            if ((code[j] != '$') && (code[j] != '_') && (!char.IsLetter(code[j])))
                return false;
            j++;
            while (j < code.Length)
            {
                if ((code[j] != '$')
                    && (code[j] != '_')
                    && (code[j] != '-')
                    && (!char.IsLetterOrDigit(code[j])))
                    break;
                j++;
            }

            if (index == j)
                return false;

            index = j;
            return true;
        }

        private static bool validateString(string code, ref int index)
        {
            int j = index;
            if (j + 1 < code.Length && ((code[j] == '\'') || (code[j] == '"')))
            {
                char fchar = code[j];
                j++;
                while (code[j] != fchar)
                {
                    if (code[j] == '\\')
                    {
                        j++;
                        if ((code[j] == '\r') && (code[j + 1] == '\n'))
                            j++;
                        else if ((code[j] == '\n') && (code[j + 1] == '\r'))
                            j++;
                    }

                    j++;
                    if (j >= code.Length)
                        return false;
                }
                index = ++j;
                return true;
            }
            return false;
        }

        public override int AttributeCount
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override string BaseURI
        {
            get
            {
                return _root.BaseURI;
            }
        }

        public override int Depth
        {
            get
            {
                return _depth;
            }
        }

        public override bool EOF
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override bool IsEmptyElement
        {
            get
            {
                return (_currentNode as XmlElement)?.IsEmpty ?? false;
            }
        }

        public override string LocalName
        {
            get
            {
                return _currentNode.LocalName;
            }
        }

        public override string NamespaceURI
        {
            get
            {
                return _currentNode.NamespaceURI;
            }
        }

        public override XmlNameTable NameTable
        {
            get
            {
                return (_root as XmlDocument ?? _root.OwnerDocument).NameTable;
            }
        }

        public override XmlNodeType NodeType
        {
            get
            {
                return _currentNode.NodeType;
            }
        }

        public override string Prefix
        {
            get
            {
                return _currentNode.Prefix;
            }
        }

        public override ReadState ReadState
        {
            get
            {
                return _readState;
            }
        }

        public override string Value
        {
            get
            {
                return _currentNode.Value;
            }
        }

        public override string GetAttribute(int i)
        {
            throw new NotImplementedException();
        }

        public override string GetAttribute(string name)
        {
            throw new NotImplementedException();
        }

        public override string GetAttribute(string name, string namespaceURI)
        {
            throw new NotImplementedException();
        }

        public override string LookupNamespace(string prefix)
        {
            throw new NotImplementedException();
        }

        public override bool MoveToAttribute(string name)
        {
            throw new NotImplementedException();
        }

        public override bool MoveToAttribute(string name, string ns)
        {
            throw new NotImplementedException();
        }

        public override bool MoveToElement()
        {
            throw new NotImplementedException();
        }

        public override bool MoveToFirstAttribute()
        {
            throw new NotImplementedException();
        }

        public override bool MoveToNextAttribute()
        {
            if (_currentNode.NodeType != XmlNodeType.Attribute)
            {
                if (_currentNode.Attributes.Count == 0)
                    return false;

                _currentNode = _currentNode.Attributes[0];

                return true;
            }
            else
            {
                var nextNode = _currentNode.NextSibling;

                if (nextNode != null)
                {
                    _currentNode = nextNode;
                    return true;
                }

                return false;
            }
        }

        public override bool Read()
        {
            if (_readState == ReadState.Initial)
            {
                _currentNode = _root.FirstChild;
                _readState = ReadState.Interactive;
                return true;
            }

            var nextNode =
                   _currentNode.FirstChild
                ?? _currentNode.NextSibling
                ?? _currentNode.ParentNode?.NextSibling;

            if (nextNode != null)
            {
                _currentNode = nextNode;
                return true;
            }

            return false;
        }

        public override bool ReadAttributeValue()
        {
            throw new NotImplementedException();
        }

        public override void ResolveEntity()
        {
            throw new NotImplementedException();
        }
    }
}
