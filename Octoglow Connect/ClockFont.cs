namespace OctoglowSender;

// The clock's fonts - appendGlyphAuto() and drawHour() in Octoglow.ino, with
// the tables they draw from - redrawn here for the "Aa" beside every font
// picker and the preview behind it. Marymba's letters are the MD_MAX72XX
// library's own font; the rest are the firmware's tables, and Italic, Topait
// and Bloc are worked out from Mako when asked for, as the clock does. The web
// interface draws them the same way (fontGlyph() in portal.html); a glyph
// changed in the firmware has to change in all three.
// A column is one byte, bit r being row r, row 0 at the top.
internal static class ClockFont
{
    // Ids 8 and 9 (Umbra, Terminal) were removed from the clock; Bloc kept 10.
    public const int Count = 11;

    // Printable ASCII, from ' ' on: sanitizeUtf8() leaves nothing else to draw.
    private const string LibraryHex =
        "0000,5f,070007,147f147f14,444aff4a32,6313086463,3649493648,07,3e4141,41413e,082a1c2a08,08083e0808,60e0,0808080"
        + "8,6060,6010080403,3e5149453e,04027f,7149494946,4149494936,0f0808087f,4f49494931,3e49494930,030101017f,36494949"
        + "36,064949493e,6c6c,6cec,081422,14141414,221408,0159090906,3e415d594e,7e0909097e,7f49494936,3e41414141,7f414141"
        + "3e,7f49494941,7f09090901,3e41414979,7f0808087f,417f41,304141413f,7f08142241,7f40404040,7f020c027f,7f0408107f,3"
        + "e4141413e,7f09090906,3e4141617e,7f09192946,4649494931,01017f0101,3f4040403f,1f2040201f,3f4038403f,6314081463,0"
        + "304780403,6151494543,7f4141,0304081060,41417f,0402010204,80808080,010204,3844447c,7f444438,38444444,3844447f,3"
        + "8545458,047e0501,18a4a47c,7f040478,7d,84857c,7f102844,7f,7c04780478,7c040478,38444438,fc242418,182424fc,7c0404"
        + "08,58545434,047f04,3c40407c,1c20407c,3c4030403c,6c10106c,1ca0a07c,6454544c,08364141,7f,41413608,02010201";

    // FONT[][5]: Marymba's digits are the firmware's, not the library's.
    private const string DigitsHex =
        "3e5149453e,00427f4000,4261514946,2141454b31,1814127f10,2745454539,3c4a494930,0171090503,3649494936,064949291e";

    // Each entry is the character, then its columns in hex.
    private const string TikoHex =
        "1427f40,03e413e,262514e,341493e,40f087f,54f4931,63e4930,701710f,83e493e,906493e,A7e097e,B7f493e,C3e4141,D7f413"
        + "e,E7f4941,F7f0901,H7f087f,I007f00,J403f00,K7f106c,L3f4040,G3e4170,M7f027f,N7f017e,O3e413e,P7f0906,Q1e215e,R7f0"
        + "976,S4e4931,T017f01,U3f403f,V3f603f,W3f4038403f,X671867,Y077807,:003600,,006000,.002000,?02510e,!005f00,%71084"
        + "7,^040304,(3e4100,)00413e,[7f4100,]00417f,<081400,>001408,'000300,;402600,/701c07,\\071c70,|007f00,\"030003,`010"
        + "200,~0c0818,Z474d71";
    private const string MakoHex =
        "03e41413e,100427f40,262514946,341494936,40c0a097f,527454539,63e494930,701710907,836494936,90649493e,A7e09097e,"
        + "B7f494936,C3e414122,D7f41413e,E7f494941,F7f090901,G3e41497a,H7f08087f,I417f41,J3040413f,K7f081463,L7f404040,M7"
        + "f020c027f,N7f06187f,O3e41413e,P7f090906,Q3e41215e,R7f091966,S46494931,T01017f0101,U3f40403f,V1f60601f,W7f20182"
        + "07f,X631c1c63,Y07787807,Z61514d43,a20545478,b7f444438,c38444444,d3844447f,e38545458,f7e0505,g98a4a47c,h7f04047"
        + "8,i7d,j807d,k7f102844,l3f40,m7c04780478,n7c040478,o38444438,pfc242418,q182424fc,r7c0804,s48545424,t043f44,u3c4"
        + "0407c,v1c60601c,w3c4030403c,x6c10106c,y1ca0a07c,z64544c44,.40,,8060,:36,;4036,!5f,?02510906,-080808,+081c08,=1"
        + "41414,/601c03,\\031c60,(3e41,)413e,[7f41,]417f,'03,\"030003,%130b3432,*0a040a,#123f123f12,_80808080,<081422,>221"
        + "408,^020102,~10081008,`0102,|7f";
    private const string BoldHex =
        "03e7f417f3e,100427f7f40,26273594f46,32263497f36,40f0f087f7f,52767457d39,63e7f497930,70161791f07,8367f497f36,90"
        + "64f497f3e,A7e7f097f7e,B7f7f497f36,C3e7f414141,D7f7f417f3e,E7f7f494941,F7f7f090901,G3e7f417979,H7f7f087f7f,I417"
        + "f7f41,J3070407f3f,K7f7f1c7763,L7f7f404040,M7f7f060c067f7f,N7f7f060c7f7f,O3e7f417f3e,P7f7f090f06,Q1e3f217f5e,R7"
        + "f7f097f76,S464f497931,T01017f7f0101,U3f7f407f3f,V1f3f603f1f,W7f7f3018307f7f,X63771c1c7763,Y03077c7c0703,Z71795"
        + "d4f47,:3636,.6060,,e060,!5f5f,?0203595f06,-080808";

    // The segments a character lights (a 1, b 2, c 4, d 8, e 16, f 32, g 64).
    private const string SegmentsHex =
        "03f,106,25b,34f,466,56d,67d,707,87f,96f,A77,B7c,C39,D5e,E79,F71,G3d,H76,I30,J1e,K74,L38,N37,O5c,P73,Q67,R50,S6"
        + "d,T78,U3e,V1c,X76,Y6e,Z5b,-40,_08,=48";

    private static readonly byte[][] Library = LibraryHex.Split(',').Select(Convert.FromHexString).ToArray();
    private static readonly byte[][] Digits = DigitsHex.Split(',').Select(Convert.FromHexString).ToArray();
    private static readonly Dictionary<char, byte[]> Tiko = Table(TikoHex);
    private static readonly Dictionary<char, byte[]> Mako = Table(MakoHex);
    private static readonly Dictionary<char, byte[]> Bold = Table(BoldHex);
    private static readonly Dictionary<char, byte> Segments = Table(SegmentsHex).ToDictionary(entry => entry.Key, entry => entry.Value[0]);

    private static Dictionary<char, byte[]> Table(string entries)
    {
        var table = new Dictionary<char, byte[]>();
        var i = 0;
        while (i < entries.Length)
        {
            var key = entries[i++];
            var end = entries.IndexOf(',', i);
            if (end < 0) end = entries.Length;
            table[key] = Convert.FromHexString(entries.AsSpan(i, end - i));
            i = end + 1;
        }
        return table;
    }

    private static byte[] Take(byte[] glyph, int size) => glyph.Length <= size ? (byte[])glyph.Clone() : glyph[..size];

    private static char Up(char ch) => ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch;

    // mx.getChar(): Marymba's letters.
    private static byte[] LibraryGlyph(char ch, int size) => ch is >= ' ' and < (char)127 ? Take(Library[ch - ' '], size) : [];

    private static byte[] TikoGlyph(char ch, int size)
    {
        if (ch == ' ') return Take([0, 0], size);
        if (ch == '-') return Take([8, 8, 8], size);
        return Take(Tiko.TryGetValue(Up(ch), out var glyph) ? glyph : [0, 0, 0], size);
    }

    private static byte[] MakoGlyph(char ch, int size)
    {
        if (ch == ' ') return Take([0, 0], size);
        return Mako.TryGetValue(ch, out var glyph) ? Take(glyph, size) : LibraryGlyph(ch, size);
    }

    private static byte[] SegmentGlyph(char ch, int size, bool filled)
    {
        var up = Up(ch);
        byte[] glyph;
        if (ch == ' ') glyph = [0, 0];
        else if (ch == ':') glyph = [0x14];
        else if (ch == '.') glyph = [0x40];
        else if (up is 'M' or 'W')
        {
            byte side = filled ? (byte)0x7F : (byte)0x36, bar = up == 'M' ? (byte)0x01 : (byte)0x40;
            glyph = [side, bar, side, bar, side];
        }
        else
        {
            if (!Segments.TryGetValue(up, out var s)) return MakoGlyph(ch, size);
            int left = 0, middle = 0, right = 0;
            if ((s & 32) != 0) left |= 0x06;
            if ((s & 16) != 0) left |= 0x30;
            if ((s & 2) != 0) right |= 0x06;
            if ((s & 4) != 0) right |= 0x30;
            if ((s & 1) != 0) middle |= 0x01;
            if ((s & 64) != 0) middle |= 0x08;
            if ((s & 8) != 0) middle |= 0x40;
            if (filled)
            {
                if ((s & 33) != 0) left |= 0x01;
                if ((s & 112) != 0) left |= 0x08;
                if ((s & 24) != 0) left |= 0x40;
                if ((s & 3) != 0) right |= 0x01;
                if ((s & 70) != 0) right |= 0x08;
                if ((s & 12) != 0) right |= 0x40;
            }
            glyph = [(byte)left, (byte)middle, (byte)middle, (byte)right];
        }
        return Take(glyph, size);
    }

    private static byte[] BoldGlyph(char ch, int size)
    {
        if (ch == ' ') return Take([0, 0], size);
        return Bold.TryGetValue(Up(ch), out var glyph) ? Take(glyph, size) : MakoGlyph(ch, size);
    }

    // Mako leaned: the top four rows one column further right.
    private static byte[] ItalicGlyph(char ch, int size)
    {
        if (ch == ' ') return Take([0, 0], size);
        var source = MakoGlyph(ch, 6);
        var n = source.Length;
        if (n == 0) return [];
        var leaned = new byte[n + 1];
        for (var c = 0; c < n; c++)
        {
            leaned[c + 1] |= (byte)(source[c] & 0x0F);
            leaned[c] |= (byte)(source[c] & 0xF0);
        }
        int first = 0, count = n + 1;
        if (ch is < '0' or > '9')
        {
            while (count > 1 && leaned[first + count - 1] == 0) count--;
            while (count > 1 && leaned[first] == 0) { first++; count--; }
        }
        return leaned.AsSpan(first, Math.Min(count, size)).ToArray();
    }

    // Topait: a character with an odd code a row lower.
    private static byte[] HopGlyph(char ch, int size)
    {
        var up = Up(ch);
        var glyph = MakoGlyph(up, size);
        if ((up & 1) == 0 || glyph.Any(column => (column & 0x80) != 0)) return glyph;
        return glyph.Select(column => (byte)(column << 1)).ToArray();
    }

    private static byte[] BlockGlyph(char ch, int size) =>
        MakoGlyph(ch, size).Select(column => (byte)(column | (column << 1))).ToArray();

    private static bool IsPixel(int mode) => mode is > 0 and < Count;

    // pixGetChar(): one character in one of the firmware's own fonts.
    private static byte[] PixelGlyph(int mode, char ch, int size) => mode switch
    {
        2 => MakoGlyph(ch, size),
        3 => SegmentGlyph(ch, size, false),
        4 => SegmentGlyph(ch, size, true),
        5 => BoldGlyph(ch, size),
        6 => ItalicGlyph(ch, size),
        7 => HopGlyph(ch, size),
        10 => BlockGlyph(ch, size),
        _ => TikoGlyph(ch, size),
    };

    // appendGlyphAuto(): a character's columns and the blank one after it.
    public static byte[] Glyph(int mode, char ch)
    {
        byte[] glyph;
        if (IsPixel(mode)) glyph = PixelGlyph(mode, ch, 7);
        else if (ch is >= '0' and <= '9') glyph = Digits[ch - '0'];
        else if (ch == ':') glyph = [0x36, 0x36];
        else if (ch == '.') glyph = [0x40];
        else glyph = LibraryGlyph(ch, 8);
        return glyph.Length == 0 ? [] : [.. glyph, 0];
    }

    // A line of text as the tiles lay it out, without the blank after the end.
    public static byte[] Text(int mode, string text)
    {
        var columns = new List<byte>();
        foreach (var ch in text) columns.AddRange(Glyph(mode, ch));
        if (columns.Count > 0) columns.RemoveAt(columns.Count - 1);
        return columns.ToArray();
    }

    // drawHour(): the time, centred the way the clock centres it.
    public static byte[] Clock(int mode, int hour, int minute, bool twoDigitHour)
    {
        var columns = new byte[32];
        var pixel = IsPixel(mode);
        byte[] Digit(int d) => pixel ? PixelGlyph(mode, (char)('0' + d), 7) : Digits[d];
        var colon = pixel ? PixelGlyph(mode, ':', 7) : [0x36, 0x36];
        var digitWidth = Digit(0).Length;
        var width = (twoDigitHour ? 2 : 1) * (digitWidth + 1) + (colon.Length + 1) + 2 * digitWidth + 1;
        var col = (32 - width) / 2;
        void Put(byte[] glyph)
        {
            foreach (var column in glyph)
            {
                if (col is >= 0 and < 32) columns[col] = column;
                col++;
            }
        }
        if (twoDigitHour)
        {
            Put(Digit(hour / 10));
            col++;
        }
        Put(Digit(hour % 10));
        col++;
        Put(colon);
        col++;
        Put(Digit(minute / 10));
        col++;
        Put(Digit(minute % 10));
        return columns;
    }

    // sanitizeUtf8(): the accented letters the clock draws plain; anything
    // else outside ASCII it leaves out.
    private static readonly (char Plain, string Accented)[] PlainLetters =
    [
        ('a', "àáâãäåăǎ"), ('A', "ÀÁÂÃÄÅĂ"), ('e', "èéêë"), ('E', "ÈÉÊË"), ('i', "ìíîïī"), ('I', "ÌÍÎÏ"),
        ('o', "òóôõöø"), ('O', "ÒÓÔÕÖØ"), ('u', "ùúûü"), ('U', "ÙÚÛÜ"), ('s', "şș"), ('S', "ŞȘ"),
        ('t', "ţț"), ('T', "ŢȚ"), ('n', "ñ"), ('N', "Ñ"), ('c', "ç"), ('C', "Ç"),
    ];

    public static string Sanitize(string text)
    {
        var plain = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch < 0x80)
            {
                plain.Append(ch);
                continue;
            }
            foreach (var (letter, accented) in PlainLetters)
            {
                if (!accented.Contains(ch)) continue;
                plain.Append(letter);
                break;
            }
        }
        return plain.ToString();
    }
}
