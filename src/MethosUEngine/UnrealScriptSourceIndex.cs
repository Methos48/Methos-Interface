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

            Token open = tokens[bodyOpen];
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

    public sealed record ClosureRepair(string FunctionName, int Line, int ClosuresInserted, int ClosuresRemoved = 0);
    public sealed record ClosureRepairResult(string Source, IReadOnlyList<ClosureRepair> Repairs);

    /// <summary>
    /// Repairs only missing closing braces at the boundary before the next function/event.
    /// Intended for source emitted by a decompiler that failed while formatting nested scopes.
    /// The original source is never modified; callers receive a repaired copy and an audit list.
    /// </summary>
    public static ClosureRepairResult RepairMissingFunctionClosures(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<Token> tokens = Tokenize(source);
        var headers = FindFunctionHeaders(tokens);
        var insertions = new List<(int Offset, string Name, int Line, int Count)>();

        for (int h = 0; h < headers.Count; h++)
        {
            var header = headers[h];
            if (header.BodyOpenIndex < 0) continue;

            int boundary = source.Length;
            for (int tokenIndex = header.BodyOpenIndex + 1; tokenIndex < tokens.Count; tokenIndex++)
            {
                if (IsDeclarationBoundary(tokens, tokenIndex))
                {
                    boundary = tokens[tokenIndex].Start;
                    break;
                }
            }
            int depth = 0;
            bool closed = false;

            for (int i = header.BodyOpenIndex; i < tokens.Count && tokens[i].Start < boundary; i++)
            {
                if (tokens[i].Text == "{") depth++;
                else if (tokens[i].Text == "}")
                {
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0) closed = true;
                    }
                    // An unmatched close is checked after missing closures have been repaired.
                    // It may be a genuine state/defaultproperties terminator outside this function.
                }
            }

            if (!closed && depth > 0)
                insertions.Add((boundary, header.Name, header.Line, depth));
        }

        string repaired = source;
        var repairs = new List<ClosureRepair>(insertions.Count);
        foreach (var item in insertions.OrderByDescending(x => x.Offset))
        {
            string closing = Environment.NewLine + new string('}', item.Count) + Environment.NewLine;
            repaired = repaired.Insert(item.Offset, closing);
            repairs.Add(new ClosureRepair(item.Name, item.Line, item.Count));
        }

        // Remove only unmatched class-level closing braces immediately before a real
        // declaration (or EOF). State and defaultproperties braces remain balanced.
        List<Token> repairedTokens = Tokenize(repaired);
        var orphanClosures = new List<(int Offset, int Line)>();
        int globalDepth = 0;
        for (int i = 0; i < repairedTokens.Count; i++)
        {
            if (repairedTokens[i].Text == "{") globalDepth++;
            else if (repairedTokens[i].Text == "}")
            {
                if (globalDepth > 0) globalDepth--;
                else if (i + 1 == repairedTokens.Count || IsDeclarationBoundary(repairedTokens, i + 1))
                    orphanClosures.Add((repairedTokens[i].Start, repairedTokens[i].Line));
            }
        }
        foreach (var orphan in orphanClosures.OrderByDescending(item => item.Offset))
        {
            repaired = repaired.Remove(orphan.Offset, 1);
            repairs.Add(new ClosureRepair("orphan", orphan.Line, 0, 1));
        }

        // Fail closed if the repaired copy is still structurally incomplete.
        _ = ParseFunctions(repaired);
        repairs.Reverse();
        return new ClosureRepairResult(repaired, repairs);
    }

    private static bool IsDeclarationBoundary(IReadOnlyList<Token> tokens, int index)
    {
        Token token = tokens[index];
        if (token.Kind != TokenKind.Identifier) return false;

        bool Is(string value, int at) =>
            at >= 0 && at < tokens.Count &&
            tokens[at].Text.Equals(value, StringComparison.OrdinalIgnoreCase);

        bool HasOpeningBraceBeforeTerminator(int start)
        {
            for (int i = start; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "{") return true;
                if (tokens[i].Text is ";" or "}") return false;
            }
            return false;
        }

        if (Is("function", index) || Is("event", index))
        {
            int open = -1;
            for (int i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "(") { open = i; break; }
                if (tokens[i].Text is ";" or "{" or "}") return false;
            }
            if (open <= index + 1) return false;
            int depth = 0;
            for (int i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "(") depth++;
                else if (tokens[i].Text == ")" && --depth == 0)
                    return HasOpeningBraceBeforeTerminator(i + 1);
            }
            return false;
        }

        if (Is("state", index))
        {
            int cursor = index + 1;
            if (Is("(", cursor))
            {
                if (!Is(")", cursor + 1)) return false;
                cursor += 2;
            }
            if (cursor >= tokens.Count || tokens[cursor].Kind != TokenKind.Identifier)
                return false;
            cursor++;
            if (Is("extends", cursor))
            {
                cursor++;
                if (cursor >= tokens.Count || tokens[cursor].Kind != TokenKind.Identifier)
                    return false;
                cursor++;
            }
            return HasOpeningBraceBeforeTerminator(cursor);
        }

        if (Is("defaultproperties", index) || Is("replication", index) || Is("cpptext", index))
            return Is("{", index + 1);

        if (Is("struct", index) || Is("enum", index))
        {
            if (index + 1 >= tokens.Count || tokens[index + 1].Kind != TokenKind.Identifier)
                return false;
            return HasOpeningBraceBeforeTerminator(index + 2);
        }

        if (Is("const", index))
        {
            bool hasEquals = false;
            for (int i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Text == "=") hasEquals = true;
                if (tokens[i].Text == ";") return hasEquals;
                if (tokens[i].Text is "{" or "}") return false;
            }
            return false;
        }

        if (Is("var", index))
        {
            for (int i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Text == ";") return true;
                if (tokens[i].Text is "{" or "}") return false;
            }
        }

        return false;
    }

    private sealed record FunctionHeader(int KeywordIndex, int BodyOpenIndex, string Name, int Line);

    private static List<FunctionHeader> FindFunctionHeaders(List<Token> tokens)
    {
        var headers = new List<FunctionHeader>();
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

            Token name = tokens[openParen - 1];
            if (name.Kind == TokenKind.Literal || name.Text.Length == 0) continue;

            int depth = 0, closeParen = -1;
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
            headers.Add(new FunctionHeader(i, bodyOpen, name.Text, keyword.Line));
        }
        return headers;
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
            if (c == '"' && i + 2 < source.Length &&
                source[i + 1] == '"' && source[i + 2] == '"')
            {
                int start = i;
                i += 3;
                tokens.Add(new Token(source[start..i], TokenKind.Literal, start, i, line));
                continue;
            }
            if (c is '"' or '\'')
            {
                char quote = c;
                int start = i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\\\' && i + 1 < source.Length)
                    {
                        if (source[i + 1] == '\n') line++;
                        i += 2;
                        continue;
                    }
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
