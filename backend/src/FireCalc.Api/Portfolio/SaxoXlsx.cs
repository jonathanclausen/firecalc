using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FireCalc.Api.Data;

namespace FireCalc.Api.Portfolio;

/// <summary>
/// Reads Saxo's transaction export (an .xlsx with one "Transaktioner" sheet). Trades carry the count and
/// price in the event text ("Købt 66 @ 46.63 EUR"); amounts are already in the account's currency.
/// Columns are found by header name, in Danish or English.
/// </summary>
public static partial class SaxoXlsx
{
    private static readonly string[] TradeDateHeaders = ["handelsdato", "trade date"];
    private static readonly string[] ValueDateHeaders = ["valørdato", "value date"];
    private static readonly string[] TypeHeaders = ["type"];
    private static readonly string[] NameHeaders = ["instrument"];
    private static readonly string[] IsinHeaders = ["instrumentets isin", "instrument isin", "isin"];
    private static readonly string[] SymbolHeaders = ["instrumentsymbol", "instrument symbol"];
    private static readonly string[] EventHeaders = ["begivenhed", "event"];
    private static readonly string[] AmountHeaders = ["beløb", "booked amount", "amount"];
    private static readonly string[] OrderHeaders = ["ordre-id", "order id"];

    [GeneratedRegex(@"^(?<side>Købt|Solgt|Bought|Sold)\s+(?<qty>-?[\d.,]+)\s*@\s*(?<price>[\d.,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TradePattern();

    public static ImportParseResult Parse(byte[] bytes)
    {
        List<List<string>> sheet;
        try
        {
            sheet = ReadFirstSheet(bytes);
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or KeyNotFoundException)
        {
            return new([], [], "This doesn't look like a Saxo export: the file is not a readable .xlsx.");
        }
        if (sheet.Count == 0) return new([], [], "The file is empty.");

        var header = sheet[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string[] names) => header.FindIndex(names.Contains);
        int tradeDate = Col(TradeDateHeaders), valueDate = Col(ValueDateHeaders), type = Col(TypeHeaders), name = Col(NameHeaders),
            isin = Col(IsinHeaders), symbol = Col(SymbolHeaders), evt = Col(EventHeaders), amount = Col(AmountHeaders), order = Col(OrderHeaders);
        if (type < 0 || evt < 0 || amount < 0 || (tradeDate < 0 && valueDate < 0))
            return new([], [], "This doesn't look like a Saxo transaction export: the date, type, event or amount column is missing.");

        var rows = new List<ImportRow>();
        var skipped = new List<ImportIssue>();
        // Rows without an order id get an id from their content; identical rows are told apart by count.
        var seen = new Dictionary<string, int>();
        for (var i = 1; i < sheet.Count; i++)
        {
            var f = sheet[i];
            string Get(int col) => col >= 0 && col < f.Count ? f[col].Trim() : "";
            if (f.All(string.IsNullOrWhiteSpace)) continue;
            var lineNo = i + 1;

            var dateText = Get(tradeDate).Length > 0 ? Get(tradeDate) : Get(valueDate);
            if (ParseDate(dateText) is not { } date)
            {
                skipped.Add(new(lineNo, "Missing date."));
                continue;
            }
            if (!decimal.TryParse(Get(amount), NumberStyles.Float, CultureInfo.InvariantCulture, out var cash))
            {
                skipped.Add(new(lineNo, "Missing amount."));
                continue;
            }

            var rawType = Get(type);
            var eventText = Get(evt);
            var isinValue = Get(isin).ToUpperInvariant();
            var hasInstrument = isinValue.Length == 12;
            decimal quantity = 0;
            decimal? price = null;
            TransactionType kind;

            var trade = TradePattern().Match(eventText);
            if (trade.Success)
            {
                var sold = trade.Groups["side"].Value.ToLowerInvariant() is "solgt" or "sold";
                kind = sold ? TransactionType.Sell : TransactionType.Buy;
                quantity = Math.Abs(ParseNumber(trade.Groups["qty"].Value) ?? 0);
                price = ParseNumber(trade.Groups["price"].Value);
                if (!hasInstrument || quantity == 0)
                {
                    skipped.Add(new(lineNo, $"\"{eventText}\" has no ISIN or quantity."));
                    continue;
                }
            }
            else if (eventText.Contains("split", StringComparison.OrdinalIgnoreCase))
            {
                // Saxo leaves out how many shares a split added, so the count has to be checked by hand.
                skipped.Add(new(lineNo, $"Share split in {Get(name)}: the file has no share count, so check how many you own."));
                continue;
            }
            else
            {
                kind = Classify(rawType, eventText, cash);
            }

            var orderId = Get(order);
            string externalId;
            if (trade.Success && orderId.Length > 0 && orderId != "0")
                externalId = "order:" + orderId;
            else
            {
                var key = string.Join('|', date.ToString("O"), rawType, eventText, isinValue, cash.ToString(CultureInfo.InvariantCulture));
                var n = seen[key] = seen.GetValueOrDefault(key) + 1;
                externalId = "row:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}#{n}")))[..32];
            }

            rows.Add(new ImportRow(
                lineNo,
                externalId,
                date,
                kind,
                eventText.Length > 0 ? eventText : rawType,
                hasInstrument ? isinValue : null,
                Get(name) is { Length: > 0 } nm ? nm : null,
                quantity,
                price,
                cash,
                hasInstrument ? YahooSymbol(Get(symbol)) : null));
        }

        return new(rows, skipped, null);
    }

    /// <summary>Maps a non-trade row by its type ("Kontantoverførsel") and event ("Indbetaling").</summary>
    public static TransactionType Classify(string rawType, string eventText, decimal amount)
    {
        var e = eventText.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(e.Contains);

        if (Has("skat", "tax")) return TransactionType.Tax;
        if (Has("udbytte", "dividend")) return TransactionType.Dividend;
        if (Has("rente", "interest")) return TransactionType.Interest;
        if (Has("gebyr", "fee", "kurtage", "commission")) return TransactionType.Fee;
        if (Has("indbetaling", "deposit")) return TransactionType.Deposit;
        if (Has("udbetaling", "withdrawal")) return TransactionType.Withdrawal;
        if (rawType.Contains("overførsel", StringComparison.OrdinalIgnoreCase) || rawType.Contains("transfer", StringComparison.OrdinalIgnoreCase))
            return amount >= 0 ? TransactionType.Deposit : TransactionType.Withdrawal;
        return TransactionType.Other;
    }

    /// <summary>
    /// Saxo writes symbols as "QDVE:xetr" or "NOVOb:xcse"; Yahoo wants "QDVE.DE" and "NOVO-B.CO".
    /// Null for exchanges we don't know, so the symbol is then looked up by ISIN as usual.
    /// </summary>
    public static string? YahooSymbol(string saxoSymbol)
    {
        var parts = saxoSymbol.Split(':');
        if (parts.Length != 2 || parts[0].Length == 0) return null;
        var suffix = parts[1].ToLowerInvariant() switch
        {
            "xnas" or "xnys" or "arcx" or "bats" or "xase" => "",
            "xetr" or "xfra" => ".DE",
            "xcse" => ".CO",
            "xsto" => ".ST",
            "xosl" => ".OL",
            "xhel" => ".HE",
            "xlon" => ".L",
            "xams" => ".AS",
            "xpar" => ".PA",
            "xbru" => ".BR",
            "xmil" => ".MI",
            "xswx" or "xvtx" => ".SW",
            _ => null,
        };
        if (suffix is null) return null;
        var code = parts[0];
        // A trailing lower-case share class ("NOVOb") is written with a dash on Yahoo ("NOVO-B").
        if (code.Length > 1 && char.IsLower(code[^1]) && code[..^1].All(c => !char.IsLower(c)))
            code = code[..^1] + "-" + char.ToUpperInvariant(code[^1]);
        return code.Replace('_', '-').ToUpperInvariant() + suffix;
    }

    /// <summary>Parses "46.63", "2,203.00" and, should Saxo write them, "2.203,00" or "46,63".</summary>
    public static decimal? ParseNumber(string text)
    {
        var s = text.Replace(" ", "").Replace(" ", "");
        if (s.Length == 0) return null;
        var comma = s.LastIndexOf(',');
        var dot = s.LastIndexOf('.');
        if (comma > dot)
        {
            // "2,203" groups thousands; "46,63" is a decimal comma.
            var decimals = s.Length - comma - 1;
            s = dot < 0 && decimals == 3 ? s.Replace(",", "") : s.Replace(".", "").Replace(',', '.');
        }
        else s = s.Replace(",", "");
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static DateOnly? ParseDate(string text)
    {
        if (text.Length == 0) return null;
        // Excel stores dates as days since 1899-12-30.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 100000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        string[] formats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yyyy", "dd-MMM-yyyy"];
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("da-DK") })
            if (DateOnly.TryParseExact(text, formats, culture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    /// <summary>Reads the first worksheet's cells as text, filling gaps so columns line up.</summary>
    private static List<List<string>> ReadFirstSheet(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument Load(string path) =>
            XDocument.Load((zip.GetEntry(path) ?? throw new KeyNotFoundException(path)).Open());

        var strings = zip.GetEntry("xl/sharedStrings.xml") is null
            ? []
            : Load("xl/sharedStrings.xml").Root!.Elements(main + "si")
                .Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value)))
                .ToList();

        var firstSheet = Load("xl/workbook.xml").Root!.Element(main + "sheets")!.Elements(main + "sheet").First();
        var relId = firstSheet.Attribute(rel + "id")!.Value;
        var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements(pkg + "Relationship")
            .First(r => r.Attribute("Id")!.Value == relId).Attribute("Target")!.Value;
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var result = new List<List<string>>();
        foreach (var row in Load(sheetPath).Descendants(main + "row"))
        {
            var cells = new List<string>();
            foreach (var c in row.Elements(main + "c"))
            {
                var column = ColumnIndex(c.Attribute("r")?.Value) ?? cells.Count;
                while (cells.Count < column) cells.Add("");
                var value = c.Element(main + "v")?.Value ?? "";
                cells.Add(c.Attribute("t")?.Value switch
                {
                    "s" => strings[int.Parse(value, CultureInfo.InvariantCulture)],
                    "inlineStr" => string.Concat(c.Descendants(main + "t").Select(t => t.Value)),
                    _ => value,
                });
            }
            result.Add(cells);
        }
        return result;
    }

    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var index = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return index - 1;
    }
}
