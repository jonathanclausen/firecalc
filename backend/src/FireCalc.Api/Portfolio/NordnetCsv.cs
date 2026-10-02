using System.Globalization;
using System.Text;
using FireCalc.Api.Data;

namespace FireCalc.Api.Portfolio;

public record ImportRow(
    int Line,
    string ExternalId,
    DateOnly Date,
    TransactionType Type,
    string RawType,
    string? Isin,
    string? Name,
    decimal Quantity,
    decimal? Price,
    decimal Amount);

public record ImportIssue(int Line, string Reason);

public record ImportParseResult(List<ImportRow> Rows, List<ImportIssue> Skipped, string? Error);

/// <summary>
/// Reads Nordnet's transaction export (Konti → Transaktioner → Eksporter). The file is usually UTF-16
/// with tabs and Danish numbers ("-4.932,1"); older exports are Latin-1 with semicolons. Columns are
/// found by header name, in Danish, Swedish, Norwegian, Finnish or English.
/// </summary>
public static class NordnetCsv
{
    private static readonly string[] IdHeaders = ["id"];
    private static readonly string[] TradeDateHeaders = ["handelsdag", "affärsdag", "handelsdato", "kauppapäivä", "trade date"];
    private static readonly string[] BookingDateHeaders = ["bogføringsdag", "bokföringsdag", "bokføringsdag", "kirjauspäivä", "booking date", "ledger date"];
    private static readonly string[] TypeHeaders = ["transaktionstype", "transaktionstyp", "transaksjonstype", "tapahtumatyyppi", "transaction type"];
    private static readonly string[] NameHeaders = ["værdipapirer", "værdipapir", "värdepapper", "verdipapir", "arvopaperi", "security", "instrument"];
    private static readonly string[] IsinHeaders = ["isin"];
    private static readonly string[] QuantityHeaders = ["antal", "antall", "määrä", "quantity"];
    private static readonly string[] PriceHeaders = ["kurs", "price"];
    private static readonly string[] AmountHeaders = ["beløb", "belopp", "beløp", "summa", "amount"];
    private static readonly string[] CancelledHeaders = ["makuleringsdato", "makuleringsdatum", "mitätöintipäivä", "cancellation date"];

    public static ImportParseResult Parse(byte[] bytes, string userCurrency)
    {
        var text = Decode(bytes);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var headerLine = lines.FindIndex(l => l.Trim().Length > 0);
        if (headerLine < 0) return new([], [], "The file is empty.");

        var delimiter = new[] { '\t', ';', ',' }.MaxBy(d => lines[headerLine].Count(c => c == d));
        var header = Split(lines[headerLine], delimiter).Select(h => h.Trim().ToLowerInvariant()).ToList();

        int Col(string[] names) => header.FindIndex(names.Contains);
        int id = Col(IdHeaders), tradeDate = Col(TradeDateHeaders), bookingDate = Col(BookingDateHeaders), type = Col(TypeHeaders),
            name = Col(NameHeaders), isin = Col(IsinHeaders), quantity = Col(QuantityHeaders), price = Col(PriceHeaders),
            amount = Col(AmountHeaders), cancelled = Col(CancelledHeaders);
        // The currency of the amount is the "Valuta" column right after it.
        var amountCurrency = amount >= 0 && amount + 1 < header.Count && header[amount + 1] is "valuta" or "valuta.1" or "currency" ? amount + 1 : -1;

        if (id < 0 || type < 0 || amount < 0 || (tradeDate < 0 && bookingDate < 0))
            return new([], [], "This doesn't look like a Nordnet transaction export: the Id, date, type or amount column is missing.");

        // English headers mean English number format; the Nordic ones use a decimal comma.
        var english = header.Contains("transaction type");

        var rows = new List<ImportRow>();
        var skipped = new List<ImportIssue>();
        for (var i = headerLine + 1; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            var lineNo = i + 1;
            var f = Split(lines[i], delimiter);
            string Get(int col) => col >= 0 && col < f.Count ? f[col].Trim() : "";

            if (Get(cancelled).Length > 0)
            {
                skipped.Add(new(lineNo, "Cancelled by the broker."));
                continue;
            }

            var externalId = Get(id);
            var dateText = Get(tradeDate).Length > 0 ? Get(tradeDate) : Get(bookingDate);
            if (externalId.Length == 0 || !DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, out var date))
            {
                skipped.Add(new(lineNo, "Missing id or date."));
                continue;
            }
            if (ParseNumber(Get(amount), english) is not { } cash)
            {
                skipped.Add(new(lineNo, "Missing amount."));
                continue;
            }
            var currency = Get(amountCurrency);
            if (currency.Length > 0 && !currency.Equals(userCurrency, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new(lineNo, $"Amount is in {currency}, not {userCurrency}."));
                continue;
            }

            var rawType = Get(type);
            var qty = Math.Abs(ParseNumber(Get(quantity), english) ?? 0);
            var isinValue = Get(isin);
            var hasInstrument = isinValue.Length == 12;
            var kind = Classify(rawType, cash, qty, hasInstrument);
            if (kind is TransactionType.Buy or TransactionType.Sell or TransactionType.SecurityIn or TransactionType.SecurityOut
                && (!hasInstrument || qty == 0))
            {
                skipped.Add(new(lineNo, $"\"{rawType}\" has no ISIN or quantity."));
                continue;
            }

            rows.Add(new ImportRow(
                lineNo,
                externalId,
                date,
                kind,
                rawType,
                hasInstrument ? isinValue.ToUpperInvariant() : null,
                Get(name) is { Length: > 0 } n ? n : null,
                qty,
                ParseNumber(Get(price), english),
                cash));
        }

        return new(rows, skipped, null);
    }

    /// <summary>Maps Nordnet's transaction type text (e.g. KØBT, SOLGT, UDBYTTE) to ours.</summary>
    public static TransactionType Classify(string rawType, decimal amount, decimal quantity, bool hasInstrument)
    {
        var t = rawType.ToUpperInvariant();
        bool Has(params string[] words) => words.Any(t.Contains);

        if (Has("KØB", "KÖP", "KJØP", "OSTO", "BUY", "BOUGHT")) return TransactionType.Buy;
        if (Has("SOLGT", "SALG", "SÅLD", "SÅLT", "MYYNTI", "SELL", "SOLD")) return TransactionType.Sell;
        if (Has("SKAT", "SKATT", "VERO", "TAX")) return TransactionType.Tax;
        if (Has("UDBYTTE", "UTDELNING", "UTBYTTE", "OSINKO", "DIVIDEND")) return TransactionType.Dividend;
        if (Has("RENTE", "RÄNTA", "KORKO", "INTEREST")) return TransactionType.Interest;
        if (Has("UDTAG", "UTTAG VP", "UTTAK VP", "SECURITY OUT", "OUTGOING")) return hasInstrument ? TransactionType.SecurityOut : TransactionType.Withdrawal;
        if (Has("INDLÆG", "INDSKUD VP", "INSÄTTNING VP", "INNSKUDD VP", "SECURITY IN", "INCOMING")) return hasInstrument ? TransactionType.SecurityIn : TransactionType.Deposit;
        if (Has("INDBETALING", "INDSÆTNING", "INSÄTTNING", "INNSKUDD", "TALLETUS", "DEPOSIT")) return TransactionType.Deposit;
        if (Has("HÆVNING", "UDBETALING", "UTTAG", "UTTAK", "NOSTO", "WITHDRAWAL")) return TransactionType.Withdrawal;
        if (Has("GEBYR", "AFGIFT", "AVGIFT", "KURTAGE", "COURTAGE", "PALKKIO", "FEE")) return TransactionType.Fee;

        // Splits, mergers and similar corporate actions move shares without money.
        if (hasInstrument && quantity > 0 && amount == 0)
            return Has("UD", "UT", "OUT") ? TransactionType.SecurityOut : TransactionType.SecurityIn;
        return TransactionType.Other;
    }

    /// <summary>Parses "-4.932,1", "70.000", "1 234,50", "NA" (none) and, for English files, "-4,932.10".</summary>
    public static decimal? ParseNumber(string text, bool english)
    {
        var s = text.Replace(" ", "").Replace(" ", "").Replace("−", "-");
        if (s.Length == 0 || s.Equals("NA", StringComparison.OrdinalIgnoreCase) || s == "-") return null;
        s = english ? s.Replace(",", "") : s.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string Decode(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        // UTF-16 without a byte order mark shows up as every other byte being zero.
        if (b.Length >= 4 && b[1] == 0 && b[3] == 0) return Encoding.Unicode.GetString(b);
        try
        {
            return new UTF8Encoding(false, true).GetString(b);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(b);
        }
    }

    private static List<string> Split(string line, char delimiter)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
