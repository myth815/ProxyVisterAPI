namespace ProxyVisterAPI.Services
{
    public interface ITextService
    {
        bool IsParagraphComplete(string paragraph);
    }

    public class TextService : ITextService
    {
        // 判断字符是否为结束符号（增加中文句号）
        private bool IsTerminator(char ch)
        {
            return ch == '.' || ch == '?' || ch == '!' || ch == '。' || ch == '？' || ch == '！';
        }

        // 判断字符是否为开放性标点符号（增加中文符号）
        private bool IsOpeningSymbol(char ch)
        {
            return ch == '(' || ch == '[' || ch == '{' || ch == '"' || ch == '“' || ch == '‘' || ch == '—'
                   || ch == '（' || ch == '【' || ch == '「' || ch == '『';
        }

        // 判断字符是否为闭合性标点符号（增加中文符号）
        private bool IsClosingSymbol(char ch, char openingSymbol)
        {
            switch (openingSymbol)
            {
                case '(':
                case '（':
                    return ch == ')' || ch == '）';
                case '[':
                case '【':
                    return ch == ']' || ch == '】';
                case '{':
                    return ch == '}';
                case '"':
                case '“':
                case '”':
                    return ch == '"' || ch == '“' || ch == '”';
                case '‘':
                case '’':
                    return ch == '‘' || ch == '’';
                case '—': // 破折号可能特殊处理，因为它自己就是闭合的
                    return ch == '—';
                case '「':
                    return ch == '」';
                case '『':
                    return ch == '』';
                default:
                    return false;
            }
        }

        // 主要的函数，用于判断一个段落是否完整
        public bool IsParagraphComplete(string paragraph)
        {
            Stack<char> symbolStack = new Stack<char>();

            for (int i = 0; i < paragraph.Length; i++)
            {
                char currentChar = paragraph[i];
                if (IsOpeningSymbol(currentChar))
                {
                    // 如果是开放性标点符号，则压入堆栈
                    symbolStack.Push(currentChar);
                }
                else if (symbolStack.Count > 0 && IsClosingSymbol(currentChar, symbolStack.Peek()))
                {
                    // 如果是闭合性标点符号，并且堆栈顶部的符号与之匹配，则出栈
                    symbolStack.Pop();
                }
            }

            // 检查堆栈是否为空且最后一个字符是否为终止符号，以确定段落的完整性
            if (paragraph.Length > 0)
            {
                return symbolStack.Count == 0 && IsTerminator(paragraph[paragraph.Length - 1]);
            }
            else
            {
                return false; // 如果字符串为空，直接返回false
            }
        }
    }
}
