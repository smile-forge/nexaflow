using System.Collections.Frozen;
using System.Globalization;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The characters HTML has names for, and what each stands for: what an entity in markdown — <c>&amp;amp;</c>,
/// <c>&amp;HilbertSpace;</c>, <c>&amp;#35;</c> — can be, as CommonMark reads one, and what it is drawn as.
///
/// <para>
/// An entity is a name from this list between <c>&amp;</c> and <c>;</c>, or a number, decimal or hexadecimal, between
/// <c>&amp;#</c> and <c>;</c>. Anything else written that way is the characters it is written with. The list is the HTML
/// standard's named character references (<c>https://html.spec.whatwg.org/entities.json</c>), taken with the semicolon
/// CommonMark asks for, each name with the code points it stands for.
/// </para>
/// </summary>
public static class MarkdownEntities
{
    /// <summary>How many characters the entity written at <paramref name="at"/> takes, or nought where none is written there.</summary>
    public static int Length(ReadOnlySpan<char> text, int at)
    {
        if (at + 2 >= text.Length || text[at] != '&') return 0;

        var next = at + 1;

        if (text[next] == '#')
        {
            next++;

            var hex = next < text.Length && text[next] is 'x' or 'X';
            if (hex) next++;

            var most = hex ? 6 : 7;
            var digits = next;

            while (next < text.Length && next - digits <= most && (hex ? char.IsAsciiHexDigit(text[next]) : char.IsAsciiDigit(text[next]))) next++;

            var count = next - digits;
            return count >= 1 && count <= most && next < text.Length && text[next] == ';' ? next + 1 - at : 0;
        }

        var start = next;
        while (next < text.Length && next - start < Longest && char.IsAsciiLetterOrDigit(text[next])) next++;

        if (next == start || next >= text.Length || text[next] != ';' || !char.IsAsciiLetter(text[start])) return 0;

        return Named.ContainsKey(text[start..next]) ? next + 1 - at : 0;
    }

    /// <summary>
    /// What <paramref name="written"/> — an entity, from its <c>&amp;</c> to its <c>;</c> — stands for, or null where it is no
    /// entity. A number naming no character, or nought, stands for the replacement character, as CommonMark has it.
    /// </summary>
    public static string? Decoded(ReadOnlySpan<char> written)
    {
        if (Length(written, 0) != written.Length) return null;

        if (written[1] != '#') return Named.TryGetValue(written[1..^1], out var named) ? named : null;

        var hex = written[2] is 'x' or 'X';
        var digits = written[(hex ? 3 : 2)..^1];
        var number = int.Parse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture);

        return number is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? "\uFFFD" : char.ConvertFromUtf32(number);
    }

    /// <summary>The longest name in the list, which is as far as a name is looked for.</summary>
    private const int Longest = 32;

    private static readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> Named = Read().GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The list, from how it is written down here: each name, an equals sign, its code points in hexadecimal, a semicolon.</summary>
    private static FrozenDictionary<string, string> Read()
    {
        var names = new Dictionary<string, string>(2200, StringComparer.Ordinal);
        var list = List.AsSpan();

        while (list.Length > 0)
        {
            var shut = list.IndexOf(';');
            var entry = list[..shut].Trim();
            list = list[(shut + 1)..];

            var equals = entry.IndexOf('=');
            var characters = string.Empty;

            foreach (var point in entry[(equals + 1)..].ToString().Split(','))
                characters += char.ConvertFromUtf32(int.Parse(point, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));

            names[entry[..equals].ToString()] = characters;
            list = list.TrimStart();
        }

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private const string List = """
        aacute=E1;Aacute=C1;abreve=103;Abreve=102;ac=223E;acd=223F;acE=223E,333;acirc=E2;Acirc=C2;acute=B4;acy=430;Acy=410;
        aelig=E6;AElig=C6;af=2061;afr=1D51E;Afr=1D504;agrave=E0;Agrave=C0;alefsym=2135;aleph=2135;alpha=3B1;Alpha=391;
        amacr=101;Amacr=100;amalg=2A3F;amp=26;AMP=26;and=2227;And=2A53;andand=2A55;andd=2A5C;andslope=2A58;andv=2A5A;
        ang=2220;ange=29A4;angle=2220;angmsd=2221;angmsdaa=29A8;angmsdab=29A9;angmsdac=29AA;angmsdad=29AB;angmsdae=29AC;
        angmsdaf=29AD;angmsdag=29AE;angmsdah=29AF;angrt=221F;angrtvb=22BE;angrtvbd=299D;angsph=2222;angst=C5;angzarr=237C;
        aogon=105;Aogon=104;aopf=1D552;Aopf=1D538;ap=2248;apacir=2A6F;ape=224A;apE=2A70;apid=224B;apos=27;
        ApplyFunction=2061;approx=2248;approxeq=224A;aring=E5;Aring=C5;ascr=1D4B6;Ascr=1D49C;Assign=2254;ast=2A;asymp=2248;
        asympeq=224D;atilde=E3;Atilde=C3;auml=E4;Auml=C4;awconint=2233;awint=2A11;backcong=224C;backepsilon=3F6;
        backprime=2035;backsim=223D;backsimeq=22CD;Backslash=2216;Barv=2AE7;barvee=22BD;barwed=2305;Barwed=2306;
        barwedge=2305;bbrk=23B5;bbrktbrk=23B6;bcong=224C;bcy=431;Bcy=411;bdquo=201E;becaus=2235;because=2235;Because=2235;
        bemptyv=29B0;bepsi=3F6;bernou=212C;Bernoullis=212C;beta=3B2;Beta=392;beth=2136;between=226C;bfr=1D51F;Bfr=1D505;
        bigcap=22C2;bigcirc=25EF;bigcup=22C3;bigodot=2A00;bigoplus=2A01;bigotimes=2A02;bigsqcup=2A06;bigstar=2605;
        bigtriangledown=25BD;bigtriangleup=25B3;biguplus=2A04;bigvee=22C1;bigwedge=22C0;bkarow=290D;blacklozenge=29EB;
        blacksquare=25AA;blacktriangle=25B4;blacktriangledown=25BE;blacktriangleleft=25C2;blacktriangleright=25B8;
        blank=2423;blk12=2592;blk14=2591;blk34=2593;block=2588;bne=3D,20E5;bnequiv=2261,20E5;bnot=2310;bNot=2AED;bopf=1D553;
        Bopf=1D539;bot=22A5;bottom=22A5;bowtie=22C8;boxbox=29C9;boxdl=2510;boxdL=2555;boxDl=2556;boxDL=2557;boxdr=250C;
        boxdR=2552;boxDr=2553;boxDR=2554;boxh=2500;boxH=2550;boxhd=252C;boxhD=2565;boxHd=2564;boxHD=2566;boxhu=2534;
        boxhU=2568;boxHu=2567;boxHU=2569;boxminus=229F;boxplus=229E;boxtimes=22A0;boxul=2518;boxuL=255B;boxUl=255C;
        boxUL=255D;boxur=2514;boxuR=2558;boxUr=2559;boxUR=255A;boxv=2502;boxV=2551;boxvh=253C;boxvH=256A;boxVh=256B;
        boxVH=256C;boxvl=2524;boxvL=2561;boxVl=2562;boxVL=2563;boxvr=251C;boxvR=255E;boxVr=255F;boxVR=2560;bprime=2035;
        breve=2D8;Breve=2D8;brvbar=A6;bscr=1D4B7;Bscr=212C;bsemi=204F;bsim=223D;bsime=22CD;bsol=5C;bsolb=29C5;bsolhsub=27C8;
        bull=2022;bullet=2022;bump=224E;bumpe=224F;bumpE=2AAE;bumpeq=224F;Bumpeq=224E;cacute=107;Cacute=106;cap=2229;
        Cap=22D2;capand=2A44;capbrcup=2A49;capcap=2A4B;capcup=2A47;capdot=2A40;CapitalDifferentialD=2145;caps=2229,FE00;
        caret=2041;caron=2C7;Cayleys=212D;ccaps=2A4D;ccaron=10D;Ccaron=10C;ccedil=E7;Ccedil=C7;ccirc=109;Ccirc=108;
        Cconint=2230;ccups=2A4C;ccupssm=2A50;cdot=10B;Cdot=10A;cedil=B8;Cedilla=B8;cemptyv=29B2;cent=A2;centerdot=B7;
        CenterDot=B7;cfr=1D520;Cfr=212D;chcy=447;CHcy=427;check=2713;checkmark=2713;chi=3C7;Chi=3A7;cir=25CB;circ=2C6;
        circeq=2257;circlearrowleft=21BA;circlearrowright=21BB;circledast=229B;circledcirc=229A;circleddash=229D;
        CircleDot=2299;circledR=AE;circledS=24C8;CircleMinus=2296;CirclePlus=2295;CircleTimes=2297;cire=2257;cirE=29C3;
        cirfnint=2A10;cirmid=2AEF;cirscir=29C2;ClockwiseContourIntegral=2232;CloseCurlyDoubleQuote=201D;
        CloseCurlyQuote=2019;clubs=2663;clubsuit=2663;colon=3A;Colon=2237;colone=2254;Colone=2A74;coloneq=2254;comma=2C;
        commat=40;comp=2201;compfn=2218;complement=2201;complexes=2102;cong=2245;congdot=2A6D;Congruent=2261;conint=222E;
        Conint=222F;ContourIntegral=222E;copf=1D554;Copf=2102;coprod=2210;Coproduct=2210;copy=A9;COPY=A9;copysr=2117;
        CounterClockwiseContourIntegral=2233;crarr=21B5;cross=2717;Cross=2A2F;cscr=1D4B8;Cscr=1D49E;csub=2ACF;csube=2AD1;
        csup=2AD0;csupe=2AD2;ctdot=22EF;cudarrl=2938;cudarrr=2935;cuepr=22DE;cuesc=22DF;cularr=21B6;cularrp=293D;cup=222A;
        Cup=22D3;cupbrcap=2A48;cupcap=2A46;CupCap=224D;cupcup=2A4A;cupdot=228D;cupor=2A45;cups=222A,FE00;curarr=21B7;
        curarrm=293C;curlyeqprec=22DE;curlyeqsucc=22DF;curlyvee=22CE;curlywedge=22CF;curren=A4;curvearrowleft=21B6;
        curvearrowright=21B7;cuvee=22CE;cuwed=22CF;cwconint=2232;cwint=2231;cylcty=232D;dagger=2020;Dagger=2021;daleth=2138;
        darr=2193;dArr=21D3;Darr=21A1;dash=2010;dashv=22A3;Dashv=2AE4;dbkarow=290F;dblac=2DD;dcaron=10F;Dcaron=10E;dcy=434;
        Dcy=414;dd=2146;DD=2145;ddagger=2021;ddarr=21CA;DDotrahd=2911;ddotseq=2A77;deg=B0;Del=2207;delta=3B4;Delta=394;
        demptyv=29B1;dfisht=297F;dfr=1D521;Dfr=1D507;dHar=2965;dharl=21C3;dharr=21C2;DiacriticalAcute=B4;DiacriticalDot=2D9;
        DiacriticalDoubleAcute=2DD;DiacriticalGrave=60;DiacriticalTilde=2DC;diam=22C4;diamond=22C4;Diamond=22C4;
        diamondsuit=2666;diams=2666;die=A8;DifferentialD=2146;digamma=3DD;disin=22F2;div=F7;divide=F7;divideontimes=22C7;
        divonx=22C7;djcy=452;DJcy=402;dlcorn=231E;dlcrop=230D;dollar=24;dopf=1D555;Dopf=1D53B;dot=2D9;Dot=A8;DotDot=20DC;
        doteq=2250;doteqdot=2251;DotEqual=2250;dotminus=2238;dotplus=2214;dotsquare=22A1;doublebarwedge=2306;
        DoubleContourIntegral=222F;DoubleDot=A8;DoubleDownArrow=21D3;DoubleLeftArrow=21D0;DoubleLeftRightArrow=21D4;
        DoubleLeftTee=2AE4;DoubleLongLeftArrow=27F8;DoubleLongLeftRightArrow=27FA;DoubleLongRightArrow=27F9;
        DoubleRightArrow=21D2;DoubleRightTee=22A8;DoubleUpArrow=21D1;DoubleUpDownArrow=21D5;DoubleVerticalBar=2225;
        downarrow=2193;Downarrow=21D3;DownArrow=2193;DownArrowBar=2913;DownArrowUpArrow=21F5;DownBreve=311;
        downdownarrows=21CA;downharpoonleft=21C3;downharpoonright=21C2;DownLeftRightVector=2950;DownLeftTeeVector=295E;
        DownLeftVector=21BD;DownLeftVectorBar=2956;DownRightTeeVector=295F;DownRightVector=21C1;DownRightVectorBar=2957;
        DownTee=22A4;DownTeeArrow=21A7;drbkarow=2910;drcorn=231F;drcrop=230C;dscr=1D4B9;Dscr=1D49F;dscy=455;DScy=405;
        dsol=29F6;dstrok=111;Dstrok=110;dtdot=22F1;dtri=25BF;dtrif=25BE;duarr=21F5;duhar=296F;dwangle=29A6;dzcy=45F;
        DZcy=40F;dzigrarr=27FF;eacute=E9;Eacute=C9;easter=2A6E;ecaron=11B;Ecaron=11A;ecir=2256;ecirc=EA;Ecirc=CA;
        ecolon=2255;ecy=44D;Ecy=42D;eDDot=2A77;edot=117;eDot=2251;Edot=116;ee=2147;efDot=2252;efr=1D522;Efr=1D508;eg=2A9A;
        egrave=E8;Egrave=C8;egs=2A96;egsdot=2A98;el=2A99;Element=2208;elinters=23E7;ell=2113;els=2A95;elsdot=2A97;emacr=113;
        Emacr=112;empty=2205;emptyset=2205;EmptySmallSquare=25FB;emptyv=2205;EmptyVerySmallSquare=25AB;emsp=2003;
        emsp13=2004;emsp14=2005;eng=14B;ENG=14A;ensp=2002;eogon=119;Eogon=118;eopf=1D556;Eopf=1D53C;epar=22D5;eparsl=29E3;
        eplus=2A71;epsi=3B5;epsilon=3B5;Epsilon=395;epsiv=3F5;eqcirc=2256;eqcolon=2255;eqsim=2242;eqslantgtr=2A96;
        eqslantless=2A95;Equal=2A75;equals=3D;EqualTilde=2242;equest=225F;Equilibrium=21CC;equiv=2261;equivDD=2A78;
        eqvparsl=29E5;erarr=2971;erDot=2253;escr=212F;Escr=2130;esdot=2250;esim=2242;Esim=2A73;eta=3B7;Eta=397;eth=F0;
        ETH=D0;euml=EB;Euml=CB;euro=20AC;excl=21;exist=2203;Exists=2203;expectation=2130;exponentiale=2147;
        ExponentialE=2147;fallingdotseq=2252;fcy=444;Fcy=424;female=2640;ffilig=FB03;fflig=FB00;ffllig=FB04;ffr=1D523;
        Ffr=1D509;filig=FB01;FilledSmallSquare=25FC;FilledVerySmallSquare=25AA;fjlig=66,6A;flat=266D;fllig=FB02;fltns=25B1;
        fnof=192;fopf=1D557;Fopf=1D53D;forall=2200;ForAll=2200;fork=22D4;forkv=2AD9;Fouriertrf=2131;fpartint=2A0D;frac12=BD;
        frac13=2153;frac14=BC;frac15=2155;frac16=2159;frac18=215B;frac23=2154;frac25=2156;frac34=BE;frac35=2157;frac38=215C;
        frac45=2158;frac56=215A;frac58=215D;frac78=215E;frasl=2044;frown=2322;fscr=1D4BB;Fscr=2131;gacute=1F5;gamma=3B3;
        Gamma=393;gammad=3DD;Gammad=3DC;gap=2A86;gbreve=11F;Gbreve=11E;Gcedil=122;gcirc=11D;Gcirc=11C;gcy=433;Gcy=413;
        gdot=121;Gdot=120;ge=2265;gE=2267;gel=22DB;gEl=2A8C;geq=2265;geqq=2267;geqslant=2A7E;ges=2A7E;gescc=2AA9;
        gesdot=2A80;gesdoto=2A82;gesdotol=2A84;gesl=22DB,FE00;gesles=2A94;gfr=1D524;Gfr=1D50A;gg=226B;Gg=22D9;ggg=22D9;
        gimel=2137;gjcy=453;GJcy=403;gl=2277;gla=2AA5;glE=2A92;glj=2AA4;gnap=2A8A;gnapprox=2A8A;gne=2A88;gnE=2269;gneq=2A88;
        gneqq=2269;gnsim=22E7;gopf=1D558;Gopf=1D53E;grave=60;GreaterEqual=2265;GreaterEqualLess=22DB;GreaterFullEqual=2267;
        GreaterGreater=2AA2;GreaterLess=2277;GreaterSlantEqual=2A7E;GreaterTilde=2273;gscr=210A;Gscr=1D4A2;gsim=2273;
        gsime=2A8E;gsiml=2A90;gt=3E;Gt=226B;GT=3E;gtcc=2AA7;gtcir=2A7A;gtdot=22D7;gtlPar=2995;gtquest=2A7C;gtrapprox=2A86;
        gtrarr=2978;gtrdot=22D7;gtreqless=22DB;gtreqqless=2A8C;gtrless=2277;gtrsim=2273;gvertneqq=2269,FE00;gvnE=2269,FE00;
        Hacek=2C7;hairsp=200A;half=BD;hamilt=210B;hardcy=44A;HARDcy=42A;harr=2194;hArr=21D4;harrcir=2948;harrw=21AD;Hat=5E;
        hbar=210F;hcirc=125;Hcirc=124;hearts=2665;heartsuit=2665;hellip=2026;hercon=22B9;hfr=1D525;Hfr=210C;
        HilbertSpace=210B;hksearow=2925;hkswarow=2926;hoarr=21FF;homtht=223B;hookleftarrow=21A9;hookrightarrow=21AA;
        hopf=1D559;Hopf=210D;horbar=2015;HorizontalLine=2500;hscr=1D4BD;Hscr=210B;hslash=210F;hstrok=127;Hstrok=126;
        HumpDownHump=224E;HumpEqual=224F;hybull=2043;hyphen=2010;iacute=ED;Iacute=CD;ic=2063;icirc=EE;Icirc=CE;icy=438;
        Icy=418;Idot=130;iecy=435;IEcy=415;iexcl=A1;iff=21D4;ifr=1D526;Ifr=2111;igrave=EC;Igrave=CC;ii=2148;iiiint=2A0C;
        iiint=222D;iinfin=29DC;iiota=2129;ijlig=133;IJlig=132;Im=2111;imacr=12B;Imacr=12A;image=2111;ImaginaryI=2148;
        imagline=2110;imagpart=2111;imath=131;imof=22B7;imped=1B5;Implies=21D2;in=2208;incare=2105;infin=221E;infintie=29DD;
        inodot=131;int=222B;Int=222C;intcal=22BA;integers=2124;Integral=222B;intercal=22BA;Intersection=22C2;intlarhk=2A17;
        intprod=2A3C;InvisibleComma=2063;InvisibleTimes=2062;iocy=451;IOcy=401;iogon=12F;Iogon=12E;iopf=1D55A;Iopf=1D540;
        iota=3B9;Iota=399;iprod=2A3C;iquest=BF;iscr=1D4BE;Iscr=2110;isin=2208;isindot=22F5;isinE=22F9;isins=22F4;
        isinsv=22F3;isinv=2208;it=2062;itilde=129;Itilde=128;iukcy=456;Iukcy=406;iuml=EF;Iuml=CF;jcirc=135;Jcirc=134;
        jcy=439;Jcy=419;jfr=1D527;Jfr=1D50D;jmath=237;jopf=1D55B;Jopf=1D541;jscr=1D4BF;Jscr=1D4A5;jsercy=458;Jsercy=408;
        jukcy=454;Jukcy=404;kappa=3BA;Kappa=39A;kappav=3F0;kcedil=137;Kcedil=136;kcy=43A;Kcy=41A;kfr=1D528;Kfr=1D50E;
        kgreen=138;khcy=445;KHcy=425;kjcy=45C;KJcy=40C;kopf=1D55C;Kopf=1D542;kscr=1D4C0;Kscr=1D4A6;lAarr=21DA;lacute=13A;
        Lacute=139;laemptyv=29B4;lagran=2112;lambda=3BB;Lambda=39B;lang=27E8;Lang=27EA;langd=2991;langle=27E8;lap=2A85;
        Laplacetrf=2112;laquo=AB;larr=2190;lArr=21D0;Larr=219E;larrb=21E4;larrbfs=291F;larrfs=291D;larrhk=21A9;larrlp=21AB;
        larrpl=2939;larrsim=2973;larrtl=21A2;lat=2AAB;latail=2919;lAtail=291B;late=2AAD;lates=2AAD,FE00;lbarr=290C;
        lBarr=290E;lbbrk=2772;lbrace=7B;lbrack=5B;lbrke=298B;lbrksld=298F;lbrkslu=298D;lcaron=13E;Lcaron=13D;lcedil=13C;
        Lcedil=13B;lceil=2308;lcub=7B;lcy=43B;Lcy=41B;ldca=2936;ldquo=201C;ldquor=201E;ldrdhar=2967;ldrushar=294B;ldsh=21B2;
        le=2264;lE=2266;LeftAngleBracket=27E8;leftarrow=2190;Leftarrow=21D0;LeftArrow=2190;LeftArrowBar=21E4;
        LeftArrowRightArrow=21C6;leftarrowtail=21A2;LeftCeiling=2308;LeftDoubleBracket=27E6;LeftDownTeeVector=2961;
        LeftDownVector=21C3;LeftDownVectorBar=2959;LeftFloor=230A;leftharpoondown=21BD;leftharpoonup=21BC;
        leftleftarrows=21C7;leftrightarrow=2194;Leftrightarrow=21D4;LeftRightArrow=2194;leftrightarrows=21C6;
        leftrightharpoons=21CB;leftrightsquigarrow=21AD;LeftRightVector=294E;LeftTee=22A3;LeftTeeArrow=21A4;
        LeftTeeVector=295A;leftthreetimes=22CB;LeftTriangle=22B2;LeftTriangleBar=29CF;LeftTriangleEqual=22B4;
        LeftUpDownVector=2951;LeftUpTeeVector=2960;LeftUpVector=21BF;LeftUpVectorBar=2958;LeftVector=21BC;
        LeftVectorBar=2952;leg=22DA;lEg=2A8B;leq=2264;leqq=2266;leqslant=2A7D;les=2A7D;lescc=2AA8;lesdot=2A7F;lesdoto=2A81;
        lesdotor=2A83;lesg=22DA,FE00;lesges=2A93;lessapprox=2A85;lessdot=22D6;lesseqgtr=22DA;lesseqqgtr=2A8B;
        LessEqualGreater=22DA;LessFullEqual=2266;LessGreater=2276;lessgtr=2276;LessLess=2AA1;lesssim=2272;
        LessSlantEqual=2A7D;LessTilde=2272;lfisht=297C;lfloor=230A;lfr=1D529;Lfr=1D50F;lg=2276;lgE=2A91;lHar=2962;
        lhard=21BD;lharu=21BC;lharul=296A;lhblk=2584;ljcy=459;LJcy=409;ll=226A;Ll=22D8;llarr=21C7;llcorner=231E;
        Lleftarrow=21DA;llhard=296B;lltri=25FA;lmidot=140;Lmidot=13F;lmoust=23B0;lmoustache=23B0;lnap=2A89;lnapprox=2A89;
        lne=2A87;lnE=2268;lneq=2A87;lneqq=2268;lnsim=22E6;loang=27EC;loarr=21FD;lobrk=27E6;longleftarrow=27F5;
        Longleftarrow=27F8;LongLeftArrow=27F5;longleftrightarrow=27F7;Longleftrightarrow=27FA;LongLeftRightArrow=27F7;
        longmapsto=27FC;longrightarrow=27F6;Longrightarrow=27F9;LongRightArrow=27F6;looparrowleft=21AB;looparrowright=21AC;
        lopar=2985;lopf=1D55D;Lopf=1D543;loplus=2A2D;lotimes=2A34;lowast=2217;lowbar=5F;LowerLeftArrow=2199;
        LowerRightArrow=2198;loz=25CA;lozenge=25CA;lozf=29EB;lpar=28;lparlt=2993;lrarr=21C6;lrcorner=231F;lrhar=21CB;
        lrhard=296D;lrm=200E;lrtri=22BF;lsaquo=2039;lscr=1D4C1;Lscr=2112;lsh=21B0;Lsh=21B0;lsim=2272;lsime=2A8D;lsimg=2A8F;
        lsqb=5B;lsquo=2018;lsquor=201A;lstrok=142;Lstrok=141;lt=3C;Lt=226A;LT=3C;ltcc=2AA6;ltcir=2A79;ltdot=22D6;
        lthree=22CB;ltimes=22C9;ltlarr=2976;ltquest=2A7B;ltri=25C3;ltrie=22B4;ltrif=25C2;ltrPar=2996;lurdshar=294A;
        luruhar=2966;lvertneqq=2268,FE00;lvnE=2268,FE00;macr=AF;male=2642;malt=2720;maltese=2720;map=21A6;Map=2905;
        mapsto=21A6;mapstodown=21A7;mapstoleft=21A4;mapstoup=21A5;marker=25AE;mcomma=2A29;mcy=43C;Mcy=41C;mdash=2014;
        mDDot=223A;measuredangle=2221;MediumSpace=205F;Mellintrf=2133;mfr=1D52A;Mfr=1D510;mho=2127;micro=B5;mid=2223;
        midast=2A;midcir=2AF0;middot=B7;minus=2212;minusb=229F;minusd=2238;minusdu=2A2A;MinusPlus=2213;mlcp=2ADB;mldr=2026;
        mnplus=2213;models=22A7;mopf=1D55E;Mopf=1D544;mp=2213;mscr=1D4C2;Mscr=2133;mstpos=223E;mu=3BC;Mu=39C;multimap=22B8;
        mumap=22B8;nabla=2207;nacute=144;Nacute=143;nang=2220,20D2;nap=2249;napE=2A70,338;napid=224B,338;napos=149;
        napprox=2249;natur=266E;natural=266E;naturals=2115;nbsp=A0;nbump=224E,338;nbumpe=224F,338;ncap=2A43;ncaron=148;
        Ncaron=147;ncedil=146;Ncedil=145;ncong=2247;ncongdot=2A6D,338;ncup=2A42;ncy=43D;Ncy=41D;ndash=2013;ne=2260;
        nearhk=2924;nearr=2197;neArr=21D7;nearrow=2197;nedot=2250,338;NegativeMediumSpace=200B;NegativeThickSpace=200B;
        NegativeThinSpace=200B;NegativeVeryThinSpace=200B;nequiv=2262;nesear=2928;nesim=2242,338;NestedGreaterGreater=226B;
        NestedLessLess=226A;NewLine=A;nexist=2204;nexists=2204;nfr=1D52B;Nfr=1D511;nge=2271;ngE=2267,338;ngeq=2271;
        ngeqq=2267,338;ngeqslant=2A7E,338;nges=2A7E,338;nGg=22D9,338;ngsim=2275;ngt=226F;nGt=226B,20D2;ngtr=226F;
        nGtv=226B,338;nharr=21AE;nhArr=21CE;nhpar=2AF2;ni=220B;nis=22FC;nisd=22FA;niv=220B;njcy=45A;NJcy=40A;nlarr=219A;
        nlArr=21CD;nldr=2025;nle=2270;nlE=2266,338;nleftarrow=219A;nLeftarrow=21CD;nleftrightarrow=21AE;
        nLeftrightarrow=21CE;nleq=2270;nleqq=2266,338;nleqslant=2A7D,338;nles=2A7D,338;nless=226E;nLl=22D8,338;nlsim=2274;
        nlt=226E;nLt=226A,20D2;nltri=22EA;nltrie=22EC;nLtv=226A,338;nmid=2224;NoBreak=2060;NonBreakingSpace=A0;nopf=1D55F;
        Nopf=2115;not=AC;Not=2AEC;NotCongruent=2262;NotCupCap=226D;NotDoubleVerticalBar=2226;NotElement=2209;NotEqual=2260;
        NotEqualTilde=2242,338;NotExists=2204;NotGreater=226F;NotGreaterEqual=2271;NotGreaterFullEqual=2267,338;
        NotGreaterGreater=226B,338;NotGreaterLess=2279;NotGreaterSlantEqual=2A7E,338;NotGreaterTilde=2275;
        NotHumpDownHump=224E,338;NotHumpEqual=224F,338;notin=2209;notindot=22F5,338;notinE=22F9,338;notinva=2209;
        notinvb=22F7;notinvc=22F6;NotLeftTriangle=22EA;NotLeftTriangleBar=29CF,338;NotLeftTriangleEqual=22EC;NotLess=226E;
        NotLessEqual=2270;NotLessGreater=2278;NotLessLess=226A,338;NotLessSlantEqual=2A7D,338;NotLessTilde=2274;
        NotNestedGreaterGreater=2AA2,338;NotNestedLessLess=2AA1,338;notni=220C;notniva=220C;notnivb=22FE;notnivc=22FD;
        NotPrecedes=2280;NotPrecedesEqual=2AAF,338;NotPrecedesSlantEqual=22E0;NotReverseElement=220C;NotRightTriangle=22EB;
        NotRightTriangleBar=29D0,338;NotRightTriangleEqual=22ED;NotSquareSubset=228F,338;NotSquareSubsetEqual=22E2;
        NotSquareSuperset=2290,338;NotSquareSupersetEqual=22E3;NotSubset=2282,20D2;NotSubsetEqual=2288;NotSucceeds=2281;
        NotSucceedsEqual=2AB0,338;NotSucceedsSlantEqual=22E1;NotSucceedsTilde=227F,338;NotSuperset=2283,20D2;
        NotSupersetEqual=2289;NotTilde=2241;NotTildeEqual=2244;NotTildeFullEqual=2247;NotTildeTilde=2249;
        NotVerticalBar=2224;npar=2226;nparallel=2226;nparsl=2AFD,20E5;npart=2202,338;npolint=2A14;npr=2280;nprcue=22E0;
        npre=2AAF,338;nprec=2280;npreceq=2AAF,338;nrarr=219B;nrArr=21CF;nrarrc=2933,338;nrarrw=219D,338;nrightarrow=219B;
        nRightarrow=21CF;nrtri=22EB;nrtrie=22ED;nsc=2281;nsccue=22E1;nsce=2AB0,338;nscr=1D4C3;Nscr=1D4A9;nshortmid=2224;
        nshortparallel=2226;nsim=2241;nsime=2244;nsimeq=2244;nsmid=2224;nspar=2226;nsqsube=22E2;nsqsupe=22E3;nsub=2284;
        nsube=2288;nsubE=2AC5,338;nsubset=2282,20D2;nsubseteq=2288;nsubseteqq=2AC5,338;nsucc=2281;nsucceq=2AB0,338;
        nsup=2285;nsupe=2289;nsupE=2AC6,338;nsupset=2283,20D2;nsupseteq=2289;nsupseteqq=2AC6,338;ntgl=2279;ntilde=F1;
        Ntilde=D1;ntlg=2278;ntriangleleft=22EA;ntrianglelefteq=22EC;ntriangleright=22EB;ntrianglerighteq=22ED;nu=3BD;Nu=39D;
        num=23;numero=2116;numsp=2007;nvap=224D,20D2;nvdash=22AC;nvDash=22AD;nVdash=22AE;nVDash=22AF;nvge=2265,20D2;
        nvgt=3E,20D2;nvHarr=2904;nvinfin=29DE;nvlArr=2902;nvle=2264,20D2;nvlt=3C,20D2;nvltrie=22B4,20D2;nvrArr=2903;
        nvrtrie=22B5,20D2;nvsim=223C,20D2;nwarhk=2923;nwarr=2196;nwArr=21D6;nwarrow=2196;nwnear=2927;oacute=F3;Oacute=D3;
        oast=229B;ocir=229A;ocirc=F4;Ocirc=D4;ocy=43E;Ocy=41E;odash=229D;odblac=151;Odblac=150;odiv=2A38;odot=2299;
        odsold=29BC;oelig=153;OElig=152;ofcir=29BF;ofr=1D52C;Ofr=1D512;ogon=2DB;ograve=F2;Ograve=D2;ogt=29C1;ohbar=29B5;
        ohm=3A9;oint=222E;olarr=21BA;olcir=29BE;olcross=29BB;oline=203E;olt=29C0;omacr=14D;Omacr=14C;omega=3C9;Omega=3A9;
        omicron=3BF;Omicron=39F;omid=29B6;ominus=2296;oopf=1D560;Oopf=1D546;opar=29B7;OpenCurlyDoubleQuote=201C;
        OpenCurlyQuote=2018;operp=29B9;oplus=2295;or=2228;Or=2A54;orarr=21BB;ord=2A5D;order=2134;orderof=2134;ordf=AA;
        ordm=BA;origof=22B6;oror=2A56;orslope=2A57;orv=2A5B;oS=24C8;oscr=2134;Oscr=1D4AA;oslash=F8;Oslash=D8;osol=2298;
        otilde=F5;Otilde=D5;otimes=2297;Otimes=2A37;otimesas=2A36;ouml=F6;Ouml=D6;ovbar=233D;OverBar=203E;OverBrace=23DE;
        OverBracket=23B4;OverParenthesis=23DC;par=2225;para=B6;parallel=2225;parsim=2AF3;parsl=2AFD;part=2202;PartialD=2202;
        pcy=43F;Pcy=41F;percnt=25;period=2E;permil=2030;perp=22A5;pertenk=2031;pfr=1D52D;Pfr=1D513;phi=3C6;Phi=3A6;phiv=3D5;
        phmmat=2133;phone=260E;pi=3C0;Pi=3A0;pitchfork=22D4;piv=3D6;planck=210F;planckh=210E;plankv=210F;plus=2B;
        plusacir=2A23;plusb=229E;pluscir=2A22;plusdo=2214;plusdu=2A25;pluse=2A72;PlusMinus=B1;plusmn=B1;plussim=2A26;
        plustwo=2A27;pm=B1;Poincareplane=210C;pointint=2A15;popf=1D561;Popf=2119;pound=A3;pr=227A;Pr=2ABB;prap=2AB7;
        prcue=227C;pre=2AAF;prE=2AB3;prec=227A;precapprox=2AB7;preccurlyeq=227C;Precedes=227A;PrecedesEqual=2AAF;
        PrecedesSlantEqual=227C;PrecedesTilde=227E;preceq=2AAF;precnapprox=2AB9;precneqq=2AB5;precnsim=22E8;precsim=227E;
        prime=2032;Prime=2033;primes=2119;prnap=2AB9;prnE=2AB5;prnsim=22E8;prod=220F;Product=220F;profalar=232E;
        profline=2312;profsurf=2313;prop=221D;Proportion=2237;Proportional=221D;propto=221D;prsim=227E;prurel=22B0;
        pscr=1D4C5;Pscr=1D4AB;psi=3C8;Psi=3A8;puncsp=2008;qfr=1D52E;Qfr=1D514;qint=2A0C;qopf=1D562;Qopf=211A;qprime=2057;
        qscr=1D4C6;Qscr=1D4AC;quaternions=210D;quatint=2A16;quest=3F;questeq=225F;quot=22;QUOT=22;rAarr=21DB;race=223D,331;
        racute=155;Racute=154;radic=221A;raemptyv=29B3;rang=27E9;Rang=27EB;rangd=2992;range=29A5;rangle=27E9;raquo=BB;
        rarr=2192;rArr=21D2;Rarr=21A0;rarrap=2975;rarrb=21E5;rarrbfs=2920;rarrc=2933;rarrfs=291E;rarrhk=21AA;rarrlp=21AC;
        rarrpl=2945;rarrsim=2974;rarrtl=21A3;Rarrtl=2916;rarrw=219D;ratail=291A;rAtail=291C;ratio=2236;rationals=211A;
        rbarr=290D;rBarr=290F;RBarr=2910;rbbrk=2773;rbrace=7D;rbrack=5D;rbrke=298C;rbrksld=298E;rbrkslu=2990;rcaron=159;
        Rcaron=158;rcedil=157;Rcedil=156;rceil=2309;rcub=7D;rcy=440;Rcy=420;rdca=2937;rdldhar=2969;rdquo=201D;rdquor=201D;
        rdsh=21B3;Re=211C;real=211C;realine=211B;realpart=211C;reals=211D;rect=25AD;reg=AE;REG=AE;ReverseElement=220B;
        ReverseEquilibrium=21CB;ReverseUpEquilibrium=296F;rfisht=297D;rfloor=230B;rfr=1D52F;Rfr=211C;rHar=2964;rhard=21C1;
        rharu=21C0;rharul=296C;rho=3C1;Rho=3A1;rhov=3F1;RightAngleBracket=27E9;rightarrow=2192;Rightarrow=21D2;
        RightArrow=2192;RightArrowBar=21E5;RightArrowLeftArrow=21C4;rightarrowtail=21A3;RightCeiling=2309;
        RightDoubleBracket=27E7;RightDownTeeVector=295D;RightDownVector=21C2;RightDownVectorBar=2955;RightFloor=230B;
        rightharpoondown=21C1;rightharpoonup=21C0;rightleftarrows=21C4;rightleftharpoons=21CC;rightrightarrows=21C9;
        rightsquigarrow=219D;RightTee=22A2;RightTeeArrow=21A6;RightTeeVector=295B;rightthreetimes=22CC;RightTriangle=22B3;
        RightTriangleBar=29D0;RightTriangleEqual=22B5;RightUpDownVector=294F;RightUpTeeVector=295C;RightUpVector=21BE;
        RightUpVectorBar=2954;RightVector=21C0;RightVectorBar=2953;ring=2DA;risingdotseq=2253;rlarr=21C4;rlhar=21CC;
        rlm=200F;rmoust=23B1;rmoustache=23B1;rnmid=2AEE;roang=27ED;roarr=21FE;robrk=27E7;ropar=2986;ropf=1D563;Ropf=211D;
        roplus=2A2E;rotimes=2A35;RoundImplies=2970;rpar=29;rpargt=2994;rppolint=2A12;rrarr=21C9;Rrightarrow=21DB;
        rsaquo=203A;rscr=1D4C7;Rscr=211B;rsh=21B1;Rsh=21B1;rsqb=5D;rsquo=2019;rsquor=2019;rthree=22CC;rtimes=22CA;rtri=25B9;
        rtrie=22B5;rtrif=25B8;rtriltri=29CE;RuleDelayed=29F4;ruluhar=2968;rx=211E;sacute=15B;Sacute=15A;sbquo=201A;sc=227B;
        Sc=2ABC;scap=2AB8;scaron=161;Scaron=160;sccue=227D;sce=2AB0;scE=2AB4;scedil=15F;Scedil=15E;scirc=15D;Scirc=15C;
        scnap=2ABA;scnE=2AB6;scnsim=22E9;scpolint=2A13;scsim=227F;scy=441;Scy=421;sdot=22C5;sdotb=22A1;sdote=2A66;
        searhk=2925;searr=2198;seArr=21D8;searrow=2198;sect=A7;semi=3B;seswar=2929;setminus=2216;setmn=2216;sext=2736;
        sfr=1D530;Sfr=1D516;sfrown=2322;sharp=266F;shchcy=449;SHCHcy=429;shcy=448;SHcy=428;ShortDownArrow=2193;
        ShortLeftArrow=2190;shortmid=2223;shortparallel=2225;ShortRightArrow=2192;ShortUpArrow=2191;shy=AD;sigma=3C3;
        Sigma=3A3;sigmaf=3C2;sigmav=3C2;sim=223C;simdot=2A6A;sime=2243;simeq=2243;simg=2A9E;simgE=2AA0;siml=2A9D;simlE=2A9F;
        simne=2246;simplus=2A24;simrarr=2972;slarr=2190;SmallCircle=2218;smallsetminus=2216;smashp=2A33;smeparsl=29E4;
        smid=2223;smile=2323;smt=2AAA;smte=2AAC;smtes=2AAC,FE00;softcy=44C;SOFTcy=42C;sol=2F;solb=29C4;solbar=233F;
        sopf=1D564;Sopf=1D54A;spades=2660;spadesuit=2660;spar=2225;sqcap=2293;sqcaps=2293,FE00;sqcup=2294;sqcups=2294,FE00;
        Sqrt=221A;sqsub=228F;sqsube=2291;sqsubset=228F;sqsubseteq=2291;sqsup=2290;sqsupe=2292;sqsupset=2290;sqsupseteq=2292;
        squ=25A1;square=25A1;Square=25A1;SquareIntersection=2293;SquareSubset=228F;SquareSubsetEqual=2291;
        SquareSuperset=2290;SquareSupersetEqual=2292;SquareUnion=2294;squarf=25AA;squf=25AA;srarr=2192;sscr=1D4C8;
        Sscr=1D4AE;ssetmn=2216;ssmile=2323;sstarf=22C6;star=2606;Star=22C6;starf=2605;straightepsilon=3F5;straightphi=3D5;
        strns=AF;sub=2282;Sub=22D0;subdot=2ABD;sube=2286;subE=2AC5;subedot=2AC3;submult=2AC1;subne=228A;subnE=2ACB;
        subplus=2ABF;subrarr=2979;subset=2282;Subset=22D0;subseteq=2286;subseteqq=2AC5;SubsetEqual=2286;subsetneq=228A;
        subsetneqq=2ACB;subsim=2AC7;subsub=2AD5;subsup=2AD3;succ=227B;succapprox=2AB8;succcurlyeq=227D;Succeeds=227B;
        SucceedsEqual=2AB0;SucceedsSlantEqual=227D;SucceedsTilde=227F;succeq=2AB0;succnapprox=2ABA;succneqq=2AB6;
        succnsim=22E9;succsim=227F;SuchThat=220B;sum=2211;Sum=2211;sung=266A;sup=2283;Sup=22D1;sup1=B9;sup2=B2;sup3=B3;
        supdot=2ABE;supdsub=2AD8;supe=2287;supE=2AC6;supedot=2AC4;Superset=2283;SupersetEqual=2287;suphsol=27C9;
        suphsub=2AD7;suplarr=297B;supmult=2AC2;supne=228B;supnE=2ACC;supplus=2AC0;supset=2283;Supset=22D1;supseteq=2287;
        supseteqq=2AC6;supsetneq=228B;supsetneqq=2ACC;supsim=2AC8;supsub=2AD4;supsup=2AD6;swarhk=2926;swarr=2199;swArr=21D9;
        swarrow=2199;swnwar=292A;szlig=DF;Tab=9;target=2316;tau=3C4;Tau=3A4;tbrk=23B4;tcaron=165;Tcaron=164;tcedil=163;
        Tcedil=162;tcy=442;Tcy=422;tdot=20DB;telrec=2315;tfr=1D531;Tfr=1D517;there4=2234;therefore=2234;Therefore=2234;
        theta=3B8;Theta=398;thetasym=3D1;thetav=3D1;thickapprox=2248;thicksim=223C;ThickSpace=205F,200A;thinsp=2009;
        ThinSpace=2009;thkap=2248;thksim=223C;thorn=FE;THORN=DE;tilde=2DC;Tilde=223C;TildeEqual=2243;TildeFullEqual=2245;
        TildeTilde=2248;times=D7;timesb=22A0;timesbar=2A31;timesd=2A30;tint=222D;toea=2928;top=22A4;topbot=2336;topcir=2AF1;
        topf=1D565;Topf=1D54B;topfork=2ADA;tosa=2929;tprime=2034;trade=2122;TRADE=2122;triangle=25B5;triangledown=25BF;
        triangleleft=25C3;trianglelefteq=22B4;triangleq=225C;triangleright=25B9;trianglerighteq=22B5;tridot=25EC;trie=225C;
        triminus=2A3A;TripleDot=20DB;triplus=2A39;trisb=29CD;tritime=2A3B;trpezium=23E2;tscr=1D4C9;Tscr=1D4AF;tscy=446;
        TScy=426;tshcy=45B;TSHcy=40B;tstrok=167;Tstrok=166;twixt=226C;twoheadleftarrow=219E;twoheadrightarrow=21A0;
        uacute=FA;Uacute=DA;uarr=2191;uArr=21D1;Uarr=219F;Uarrocir=2949;ubrcy=45E;Ubrcy=40E;ubreve=16D;Ubreve=16C;ucirc=FB;
        Ucirc=DB;ucy=443;Ucy=423;udarr=21C5;udblac=171;Udblac=170;udhar=296E;ufisht=297E;ufr=1D532;Ufr=1D518;ugrave=F9;
        Ugrave=D9;uHar=2963;uharl=21BF;uharr=21BE;uhblk=2580;ulcorn=231C;ulcorner=231C;ulcrop=230F;ultri=25F8;umacr=16B;
        Umacr=16A;uml=A8;UnderBar=5F;UnderBrace=23DF;UnderBracket=23B5;UnderParenthesis=23DD;Union=22C3;UnionPlus=228E;
        uogon=173;Uogon=172;uopf=1D566;Uopf=1D54C;uparrow=2191;Uparrow=21D1;UpArrow=2191;UpArrowBar=2912;
        UpArrowDownArrow=21C5;updownarrow=2195;Updownarrow=21D5;UpDownArrow=2195;UpEquilibrium=296E;upharpoonleft=21BF;
        upharpoonright=21BE;uplus=228E;UpperLeftArrow=2196;UpperRightArrow=2197;upsi=3C5;Upsi=3D2;upsih=3D2;upsilon=3C5;
        Upsilon=3A5;UpTee=22A5;UpTeeArrow=21A5;upuparrows=21C8;urcorn=231D;urcorner=231D;urcrop=230E;uring=16F;Uring=16E;
        urtri=25F9;uscr=1D4CA;Uscr=1D4B0;utdot=22F0;utilde=169;Utilde=168;utri=25B5;utrif=25B4;uuarr=21C8;uuml=FC;Uuml=DC;
        uwangle=29A7;vangrt=299C;varepsilon=3F5;varkappa=3F0;varnothing=2205;varphi=3D5;varpi=3D6;varpropto=221D;varr=2195;
        vArr=21D5;varrho=3F1;varsigma=3C2;varsubsetneq=228A,FE00;varsubsetneqq=2ACB,FE00;varsupsetneq=228B,FE00;
        varsupsetneqq=2ACC,FE00;vartheta=3D1;vartriangleleft=22B2;vartriangleright=22B3;vBar=2AE8;Vbar=2AEB;vBarv=2AE9;
        vcy=432;Vcy=412;vdash=22A2;vDash=22A8;Vdash=22A9;VDash=22AB;Vdashl=2AE6;vee=2228;Vee=22C1;veebar=22BB;veeeq=225A;
        vellip=22EE;verbar=7C;Verbar=2016;vert=7C;Vert=2016;VerticalBar=2223;VerticalLine=7C;VerticalSeparator=2758;
        VerticalTilde=2240;VeryThinSpace=200A;vfr=1D533;Vfr=1D519;vltri=22B2;vnsub=2282,20D2;vnsup=2283,20D2;vopf=1D567;
        Vopf=1D54D;vprop=221D;vrtri=22B3;vscr=1D4CB;Vscr=1D4B1;vsubne=228A,FE00;vsubnE=2ACB,FE00;vsupne=228B,FE00;
        vsupnE=2ACC,FE00;Vvdash=22AA;vzigzag=299A;wcirc=175;Wcirc=174;wedbar=2A5F;wedge=2227;Wedge=22C0;wedgeq=2259;
        weierp=2118;wfr=1D534;Wfr=1D51A;wopf=1D568;Wopf=1D54E;wp=2118;wr=2240;wreath=2240;wscr=1D4CC;Wscr=1D4B2;xcap=22C2;
        xcirc=25EF;xcup=22C3;xdtri=25BD;xfr=1D535;Xfr=1D51B;xharr=27F7;xhArr=27FA;xi=3BE;Xi=39E;xlarr=27F5;xlArr=27F8;
        xmap=27FC;xnis=22FB;xodot=2A00;xopf=1D569;Xopf=1D54F;xoplus=2A01;xotime=2A02;xrarr=27F6;xrArr=27F9;xscr=1D4CD;
        Xscr=1D4B3;xsqcup=2A06;xuplus=2A04;xutri=25B3;xvee=22C1;xwedge=22C0;yacute=FD;Yacute=DD;yacy=44F;YAcy=42F;ycirc=177;
        Ycirc=176;ycy=44B;Ycy=42B;yen=A5;yfr=1D536;Yfr=1D51C;yicy=457;YIcy=407;yopf=1D56A;Yopf=1D550;yscr=1D4CE;Yscr=1D4B4;
        yucy=44E;YUcy=42E;yuml=FF;Yuml=178;zacute=17A;Zacute=179;zcaron=17E;Zcaron=17D;zcy=437;Zcy=417;zdot=17C;Zdot=17B;
        zeetrf=2128;ZeroWidthSpace=200B;zeta=3B6;Zeta=396;zfr=1D537;Zfr=2128;zhcy=436;ZHcy=416;zigrarr=21DD;zopf=1D56B;
        Zopf=2124;zscr=1D4CF;Zscr=1D4B5;zwj=200D;zwnj=200C;
        """;
}
