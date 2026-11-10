namespace MethosUEngine;

public sealed record UnrealFunctionSource(string Name, string Body, int DeclarationOffset,
    int BodyOffset, int BodyLength, int Line);

/// <summary>
/// Locates UnrealScript function bodies while ignoring braces and keywords inside
/// comments, string literals, name literals and preprocessor lines.
/// This is a source indexer, not yet the full semantic compiler.
/// </summary>
public static class UnrealScriptSourceIndex
{
    private enum TokenKind { Identifier, Symbol, Literal }
    private readonly record struct Token(string Text, TokenKind Kind, int Start, int End, int Line);

    public static IReadOnlyList<UnrealFunctionSource> ParseFunctions(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<Token> tokens = Tokenize(source);
        var functions = new List<UnrealFunctionSource>();

        for (int i = 0; i < tokens.Count; i++)
        {
            Token keyword = tokens[i];
            if (keyword.Kind != TokenKind.Identifier ||
                !(keyword.Text.Equals("function", StringComparison.OrdinalIgnoreCase) ||
                  keyword.Text.Equals("event", StringComparison.OrdinalIgnoreCase)))
                continue;

            int openParen = -1;
            for (int j = i + 1; j < tokens.Count && j < i + 96; j++)
            {
                if (tokens[j].Text == "(") { openParen = j; break; }
                if (tokens[j].Text is ";" or "{" or "}") break;
            }
            if (openParen <= i + 1) continue;

            Token nameToken = tokens[openParen - 1];
            if (nameToken.Kind == TokenKind.Literal || nameToken.Text.Length == 0) continue;

            int depth = 0;
            int closeParen = -1;
            for (int j = openParen; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "(") depth++;
                else if (tokens[j].Text == ")" && --depth == 0) { closeParen = j; break; }
            }
            if (closeParen < 0) continue;

            int bodyOpen = -1;
            for (int j = closeParen + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "{") { bodyOpen = j; break; }
                if (tokens[j].Text is ";" or "}") break;
            }
            if (bodyOpen < 0) { i = closeParen; continue; }

            depth = 0;
            int bodyClose = -1;
            for (int j = bodyOpen; j < tokens.Count; j++)
            {
                if (tokens[j].Text == "{") depth++;
                else if (tokens[j].Text == "}" && --depth == 0) { bodyClose = j; break; }
            }
            if (bodyClose < 0)
            {
                string excerpt = source.Substring(open.Start, Math.Min(900, source.Length - open.Start));
                throw new InvalidDataException($"La función '{nameToken.Text}' (línea {keyword.Line}) tiene un bloque sin cerrar; apertura en línea {open.Line}, offset {open.Start}. Fragmento: {excerpt}");
            }

            Token open = tokens[bodyOpen];
            Token close = tokens[bodyClose];
            int bodyStart = open.End;
            int bodyEnd = close.Start;
            functions.Add(new UnrealFunctionSource(nameToken.Text,
                source.Substring(bodyStart, bodyEnd - bodyStart),
                keyword.Start, bodyStart, bodyEnd - bodyStart, keyword.Line));
            i = bodyClose;
        }

        return functions;
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0, line = 1;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                int commentDepth = 1;
                while (i < source.Length && commentDepth > 0)
                {
                    if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                    {
                        commentDepth++; i += 2; continue;
                    }
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        commentDepth--; i += 2; continue;
                    }
                    if (source[i] == '\n') line++;
                    i++;
                }
                if (commentDepth != 0) throw new InvalidDataException("Comentario de bloque sin cerrar.");
                continue;
            }
            if (c == '#')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c is '"' or '\'')
            {
                char quote = c;
                int start = i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\') { i = Math.Min(source.Length, i + 2); continue; }
                    if (source[i] == '\n') line++;
                    if (source[i++] == quote) break;
                }
                tokens.Add(new Token(source[start..i], TokenKind.Literal, start, i, line));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(source[start..i], TokenKind.Identifier, start, i, line));
                continue;
            }

            int symbolStart = i;
            string symbol;
            if (i + 2 < source.Length && source.AsSpan(i, 3).SequenceEqual(">>>"))
            { symbol = ">>>"; i += 3; }
            else if (i + 1 < source.Length && new[] { "++", "--", "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "*=", "/=", "@=", "$=", "&=", "|=", "^=", "<<", ">>", "**", "~=", "^^" }.Contains(source.Substring(i, 2)))
            { symbol = source.Substring(i, 2); i += 2; }
            else { symbol = source[i++].ToString(); }
            tokens.Add(new Token(symbol, TokenKind.Symbol, symbolStart, i, line));
        }
        return tokens;
    }
}
