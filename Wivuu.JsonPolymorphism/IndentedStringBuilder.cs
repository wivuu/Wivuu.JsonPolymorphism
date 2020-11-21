using System;
using System.Collections.Generic;
using System.Text;

namespace Wivuu.JsonPolymorphism
{
    public class IndentedStringBuilder
    {
        readonly StringBuilder _internal;

        int _indentation = 0;
        string _indent   = "";

        public int SpacePerIndent { get; set; } = 4;

        public IndentedStringBuilder() =>
            _internal = new StringBuilder();

        public IndentedStringBuilder(string value) =>
            _internal = new StringBuilder(value);

        private StringBuilder DoIndent() => _internal.Append(_indent);

        public IndentedStringBuilder AppendLine()
        {
            _internal.AppendLine();
            return this;
        }

        public IndentedStringBuilder AppendLine(string value)
        {
            DoIndent().AppendLine(value);
            return this;
        }

        public IndentedStringBuilder Append(string value)
        {
            _internal.Append(value);
            return this;
        }

        public IndentedStringBuilder AppendIndent(string value)
        {
            DoIndent().Append(value);
            return this;
        }

        public IDisposable Indent(char ch, string? endCh = null, int level = 1)
        {
            DoIndent().Append(ch).AppendLine();

            _indentation += level;
            _indent = new string(' ', _indentation * SpacePerIndent);

            return new DisposableCallback(() =>
            {
                _indentation -= level;
                _indent = new string(' ', _indentation * SpacePerIndent);

                if (endCh is string ending)
                    DoIndent().Append(endCh).AppendLine();
                else
                    DoIndent().Append(
                        ch switch
                        {
                            '{' => '}',
                            '(' => ')',
                            '[' => ']',
                            _   => ch
                        }
                    )
                    .AppendLine();
            });
        }

        public IndentedStringBuilder Indent(char ch, Action<IndentedStringBuilder> callback, string? endCh = null, int level = 1)
        {
            using (Indent(ch, endCh, level))
                callback(this);

            return this;
        }

        public IDisposable Indent(int level = 1)
        {
            _indentation += level;
            _indent = new string(' ', _indentation * SpacePerIndent);

            return new DisposableCallback(() =>
            {
                _indentation -= level;
                _indent = new string(' ', _indentation * SpacePerIndent);
            });
        }

        public override string ToString()
        {
            return _internal.ToString();
        }

        private class DisposableCallback : IDisposable
        {
            private Action Callback;

            public DisposableCallback(Action cb) => Callback = cb;

            public void Dispose() => Callback();
        }
    }
}
