namespace LibRed.Catalog;

/// <summary>
/// A text collating order, identified by its Windows locale id (LCID) — the value Jet/ACE stores in a
/// column descriptor's locale bytes (<c>0x0B/0x0C</c>) and, database-wide, in the page-0 sort order.
/// These mirror DAO's <c>CollatingOrderEnum</c>; each name is the LCID Access records. That is a Jet-3.5-era
/// list and no longer matches what ACE offers, in both directions: Access's "New Database Sort Order" adds
/// Bosnian, Croatian, Serbian, Macedonian, Ukrainian, Estonian, Latvian, Lithuanian, Slovak, Romanian,
/// Georgian Modern, Vietnamese, Indic, French, German Phone Book, Hungarian Technical and the CJK variants,
/// and offers none of the five marked <i>inert</i> below. Those five are still creatable through DAO and are
/// recorded faithfully on page 0 and in column descriptors, but ACE encodes **General** keys for them
/// regardless — verified over 82 samples in <c>DaoLocaleCollationProbeTest</c>. Treat them as metadata.
/// <para>
/// That sample set includes words carrying TWO marks in each script, which is the shape that matters: French
/// tailors no letter at all and would look inert on single characters, because it reverses the diacritic
/// section and a word with one accent encodes identically to General. The Greek triple
/// <c>άα</c>/<c>αά</c>/<c>άαά</c> is the direct analogue of the French <c>coté</c>/<c>côte</c>/<c>côté</c>
/// that exposed it. The probe also carries Spanish, Czech, Polish and Turkish as positive controls, so a
/// null result means the orders are inert rather than the harness being dead.
/// </para>
/// </summary>
/// <remarks>
/// The LCID alone does not pin the on-disk key bytes: "General" (1033) has a legacy order (version 0,
/// Access 2000–2007) and a different default order (version 1, Access 2010+). The version lives in a
/// separate descriptor byte — see <see cref="Collation"/>. The two axes are independent: both Spanish orders
/// are version 0, so the version selects a weight-table generation rather than a locale variant. Paradox-ISAM
/// variants that DAO lists share LCIDs with these (e.g. dbSortPDXIntl == 1033) and are omitted; they are
/// link-only and irrelevant here.
/// </remarks>
public enum CollatingOrder
{
    Undefined = -1,
    Neutral = 1024,
    Arabic = 1025,           // inert: recorded, but ACE encodes General keys — see the remarks below
    ChineseTraditional = 1028,
    Czech = 1029,
    NorwegianDanish = 1030,
    Greek = 1032,            // inert
    German = 1031,           // with sort id 1 = "German Phone Book"
    General = 1033,          // English, German, French, Portuguese — the default
    Spanish = 1034,          // Spanish Traditional: "ch" and "ll" are letters (DAO's dbSortSpanish)
    Finnish = 1035,          // = SwedishFinnish 1053, measured
    French = 1036,
    Hebrew = 1037,           // inert
    Hungarian = 1038,        // with sort id 1 = "Hungarian Technical"
    Icelandic = 1039,
    Japanese = 1041,
    Korean = 1042,
    Dutch = 1043,            // inert
    Norwegian = 1044,        // Access's "Norwegian/Danish" — note DAO's dbSortNorwDan is Danish 1030 instead
    Polish = 1045,
    Romanian = 1048,
    Cyrillic = 1049,         // inert
    Croatian = 1050,
    Slovak = 1051,
    SwedishFinnish = 1053,
    Thai = 1054,
    Turkish = 1055,
    Ukrainian = 1058,
    Slovenian = 1060,
    Estonian = 1061,
    Latvian = 1062,
    Lithuanian = 1063,
    Vietnamese = 1066,
    Macedonian = 1071,
    Georgian = 1079,         // with sort id 1 = "Georgian Modern"
    Indic = 1081,
    ChineseSimplified = 2052,
    SpanishMexico = 2058,    // = Spanish TRADITIONAL 1034, not Modern — measured, and the two differ
    FrenchBelgium = 2060,    // = French 1036, measured
    NorwegianNynorsk = 2068, // = Norwegian 1044, measured
    Serbian = 2074,
    SwedishFinland = 2077,   // = SwedishFinnish 1053, measured
    SpanishModern = 3082,    // The 1994 reform: "ch"/"ll" are letter pairs. No DAO name — it postdates the enum
    FrenchCanada = 3084,     // = French 1036, measured
    Bosnian = 5146,

    // ---------------------------------------------------------------------------------------------------
    // Orders measured to produce index keys BYTE-IDENTICAL to General v0 — every locale DAO will create
    // that is not named above. They are listed because they exist on disk and a file can be written in any
    // of them, not because they sort differently: none carries a tailoring, and `JetLocaleTailoring`'s
    // GeneralV0 set is what admits them.
    //
    // Naming an order that behaves exactly like General is the convention already: Arabic, Greek, Hebrew,
    // Dutch and Cyrillic sat above marked "inert" on that basis, before the survey measured the other 102
    // and found them the same. Grouped by script rather than interleaved above, because the split is real —
    // above is "orders with behaviour", here is "orders with an identity and nothing else".
    //
    // Measured in CollationSurveyProbeTests, each against ACE's own keys over its script's whole Unicode
    // block plus the Latin baseline. Seven of the surveyed orders are NOT here because they do differ; they
    // are the aliases above.
    // ---------------------------------------------------------------------------------------------------

    // Latin — western Europe (German 1031, Dutch 1043 named above)
    Catalan = 1027,
    Italian = 1040,
    PortugueseBrazil = 1046,
    Romansh = 1047,
    Albanian = 1052,
    Basque = 1069,

    // Latin — sublanguage variants
    GermanSwitzerland = 2055,
    EnglishUnitedKingdom = 2057,
    ItalianSwitzerland = 2064,
    DutchBelgium = 2067,
    PortuguesePortugal = 2070,
    GermanAustria = 3079,
    EnglishAustralia = 3081,

    // Latin — central and northern Europe
    UpperSorbian = 1070,
    Afrikaans = 1078,
    Faroese = 1080,
    Maltese = 1082,
    SamiNorthern = 1083,
    Welsh = 1106,
    Galician = 1110,
    Frisian = 1122,
    Luxembourgish = 1134,
    Greenlandic = 1135,
    Breton = 1150,
    Occitan = 1154,
    Corsican = 1155,
    Alsatian = 1156,
    ScottishGaelic = 1169,

    // Latin — Turkic, African, Asian, American
    Indonesian = 1057,
    AzerbaijaniLatin = 1068,
    Tswana = 1074,
    Xhosa = 1076,
    Zulu = 1077,
    Malay = 1086,
    Swahili = 1089,
    Turkmen = 1090,
    UzbekLatin = 1091,
    TamazightLatin = 1119,
    Filipino = 1124,
    Hausa = 1128,
    Yoruba = 1130,
    Quechua = 1131,
    SesothoSaLeboa = 1132,
    Igbo = 1136,
    Mapudungun = 1146,
    Mohawk = 1148,
    Maori = 1153,
    Kiche = 1158,
    Kinyarwanda = 1159,
    Wolof = 1160,
    InuktitutLatin = 2141,

    // Cyrillic (Russian is `Cyrillic` 1049, named above)
    Bulgarian = 1026,
    Belarusian = 1059,
    Tajik = 1064,
    Kazakh = 1087,
    Kyrgyz = 1088,
    Tatar = 1092,
    MongolianCyrillic = 1104,
    Bashkir = 1133,
    Sakha = 1157,
    AzerbaijaniCyrillic = 2092,
    UzbekCyrillic = 2115,
    SerbianCyrillic = 3098,
    BosnianCyrillic = 8218,
    CroatianBosniaHerzegovina = 4122,

    // Caucasus (Georgian 1079 is named above; at sort id 0 it is one of these, at sort id 1 it is tailored)
    Armenian = 1067,

    // Arabic script and the other RTL orders (Arabic 1025, Hebrew 1037 named above)
    UrduPakistan = 1056,
    Persian = 1065,
    Yiddish = 1085,
    Syriac = 1114,
    Pashto = 1123,
    Divehi = 1125,
    Uighur = 1152,
    Dari = 1164,
    CentralKurdish = 1170,
    ArabicIraq = 2049,
    UrduIndia = 2080,

    // Devanagari and the eastern Indic scripts (Hindi 1081 is `Indic`, above, and exists only at version 1)
    Bengali = 1093,
    Punjabi = 1094,
    Gujarati = 1095,
    Odia = 1096,
    Assamese = 1101,
    Marathi = 1102,
    Sanskrit = 1103,
    Konkani = 1111,
    Nepali = 1121,

    // The southern Indic scripts
    Tamil = 1097,
    Telugu = 1098,
    Kannada = 1099,
    Malayalam = 1100,
    Sinhala = 1115,

    // The remaining scripts
    Tibetan = 1105,
    Khmer = 1107,
    Lao = 1108,
    Cherokee = 1116,
    InuktitutSyllabics = 1117,
    Amharic = 1118,
    Tigrinya = 1139,
    Yi = 1144,
    MongolianTraditional = 2128,

    // ---------------------------------------------------------------------------------------------------
    // The rest of the reachable surface: every remaining LANGID Windows defines a culture for and DAO will
    // accept. Access's own dropdown offers a couple of dozen orders; DAO takes a RAW LANGID and Jet stores it
    // verbatim, so this is what a file can actually carry.
    //
    // Two families the shorter list above misses entirely:
    //
    //  * NEUTRAL LANGIDs — sublanguage 0, or a script with no region. Jet accepts them and stores them as
    //    collating orders 1..145 and 25626..31847, which are not LCIDs any locale picker will show you. Each
    //    resolves to its language's own order, so `cs` (5) IS Czech and `hr` (26) IS Croatian, not General.
    //
    //  * SUBLANGUAGES — es-AR, en-CA, ar-EG and 150 more. These do NOT reliably inherit from their base
    //    language, which is the single most important thing measured here. Spanish alone splits three ways:
    //    `es` and es-MX are Traditional, seventeen Latin-American variants are Modern, and es-US, es-419 and
    //    es-CU are plain General. French splits too — fr-CH/LU/MC take the French order while fr-CD, fr-SN,
    //    fr-CI and six more do not.
    //
    // Names come from the culture's English name, ASCII-folded, with a Neutral suffix where the culture is
    // region-neutral (its language usually has a region-specific member too, and they are different orders:
    // Bengali is 1093 and the neutral `bn` is 69). Generated and re-generatable — see the emitter in
    // CollationSurveyProbeTests, which reads the batch reports and writes this block, so no LCID here was
    // retyped by hand.
    // ---------------------------------------------------------------------------------------------------

    // --- Neutral: language only ---
    ArabicNeutral = 1,
    BulgarianNeutral = 2,
    CatalanNeutral = 3,
    CzechNeutral = 5,
    DanishNeutral = 6,
    GermanNeutral = 7,
    GreekNeutral = 8,
    EnglishNeutral = 9,
    SpanishNeutral = 10,
    FinnishNeutral = 11,
    FrenchNeutral = 12,
    HebrewNeutral = 13,
    HungarianNeutral = 14,
    IcelandicNeutral = 15,
    ItalianNeutral = 16,
    DutchNeutral = 19,
    NorwegianNeutral = 20,
    PolishNeutral = 21,
    PortugueseNeutral = 22,
    RomanshNeutral = 23,
    RomanianNeutral = 24,
    RussianNeutral = 25,
    CroatianNeutral = 26,
    SlovakNeutral = 27,
    AlbanianNeutral = 28,
    SwedishNeutral = 29,
    ThaiNeutral = 30,
    TurkishNeutral = 31,
    UrduNeutral = 32,
    IndonesianNeutral = 33,
    UkrainianNeutral = 34,
    BelarusianNeutral = 35,
    SlovenianNeutral = 36,
    EstonianNeutral = 37,
    LatvianNeutral = 38,
    LithuanianNeutral = 39,
    TajikNeutral = 40,
    PersianNeutral = 41,
    VietnameseNeutral = 42,
    ArmenianNeutral = 43,
    AzerbaijaniNeutral = 44,
    BasqueNeutral = 45,
    UpperSorbianNeutral = 46,
    MacedonianNeutral = 47,
    SesothoNeutral = 48,
    XitsongaNeutral = 49,
    SetswanaNeutral = 50,
    VendaNeutral = 51,
    IsiXhosaNeutral = 52,
    IsiZuluNeutral = 53,
    AfrikaansNeutral = 54,
    GeorgianNeutral = 55,
    FaroeseNeutral = 56,
    HindiNeutral = 57,
    MalteseNeutral = 58,
    NorthernSamiNeutral = 59,
    IrishNeutral = 60,
    YiddishNeutral = 61,
    MalayNeutral = 62,
    KazakhNeutral = 63,
    KyrgyzNeutral = 64,
    KiswahiliNeutral = 65,
    TurkmenNeutral = 66,
    UzbekNeutral = 67,
    TatarNeutral = 68,
    BanglaNeutral = 69,
    PunjabiNeutral = 70,
    GujaratiNeutral = 71,
    OdiaNeutral = 72,
    TamilNeutral = 73,
    TeluguNeutral = 74,
    KannadaNeutral = 75,
    MalayalamNeutral = 76,
    AssameseNeutral = 77,
    MarathiNeutral = 78,
    SanskritNeutral = 79,
    MongolianNeutral = 80,
    TibetanNeutral = 81,
    WelshNeutral = 82,
    KhmerNeutral = 83,
    LaoNeutral = 84,
    BurmeseNeutral = 85,
    GalicianNeutral = 86,
    KonkaniNeutral = 87,
    ManipuriNeutral = 88,
    SindhiNeutral = 89,
    SyriacNeutral = 90,
    SinhalaNeutral = 91,
    CherokeeNeutral = 92,
    InuktitutNeutral = 93,
    AmharicNeutral = 94,
    CentralAtlasTamazightNeutral = 95,
    KashmiriNeutral = 96,
    NepaliNeutral = 97,
    WesternFrisianNeutral = 98,
    PashtoNeutral = 99,
    FilipinoNeutral = 100,
    DivehiNeutral = 101,
    EdoNeutral = 102,
    FulaNeutral = 103,
    HausaNeutral = 104,
    IbibioNeutral = 105,
    YorubaNeutral = 106,
    SesothoSaLeboaNeutral = 108,
    BashkirNeutral = 109,
    LuxembourgishNeutral = 110,
    KalaallisutNeutral = 111,
    IgboNeutral = 112,
    KanuriNeutral = 113,
    OromoNeutral = 114,
    TigrinyaNeutral = 115,
    GuaraniNeutral = 116,
    HawaiianNeutral = 117,
    LatinNeutral = 118,
    SomaliNeutral = 119,
    YiNeutral = 120,
    PapiamentoNeutral = 121,
    MapucheNeutral = 122,
    MohawkNeutral = 124,
    BretonNeutral = 126,
    UyghurNeutral = 128,
    MaoriNeutral = 129,
    OccitanNeutral = 130,
    CorsicanNeutral = 131,
    SwissGermanNeutral = 132,
    YakutNeutral = 133,
    KIcheNeutral = 134,
    KinyarwandaNeutral = 135,
    WolofNeutral = 136,
    ScottishGaelicNeutral = 145,

    // --- Region-specific ---
    SesothoSouthAfrica = 1072,
    XitsongaSouthAfrica = 1073,
    VendaSouthAfrica = 1075,
    BurmeseMyanmar = 1109,
    SindhiDevanagariIndia = 1113,
    KashmiriArabicNeutral = 1120,
    EdoNigeria = 1126,
    IbibioNigeria = 1129,
    OromoEthiopia = 1138,
    GuaraniParaguay = 1140,
    HawaiianUnitedStates = 1141,
    SomaliSomalia = 1143,
    PapiamentoCaribbean = 1145,
    RomanianMoldova = 2072,
    RussianMoldova = 2073,
    LowerSorbianGermany = 2094,
    SetswanaBotswana = 2098,
    NorthernSamiSweden = 2107,
    IrishIreland = 2108,
    MalayBrunei = 2110,
    BanglaBangladesh = 2117,
    PunjabiArabicPakistan = 2118,
    TamilSriLanka = 2121,
    SindhiArabicPakistan = 2137,
    KashmiriDevanagariIndia = 2144,
    NepaliIndia = 2145,
    FulaLatinSenegal = 2151,
    TigrinyaEritrea = 2163,
    ArabicEgypt = 3073,
    NorthernSamiFinland = 3131,
    MongolianMongolianMongolia = 3152,
    DzongkhaBhutan = 3153,
    ArabicLibya = 4097,
    GermanLuxembourg = 4103,
    EnglishCanada = 4105,
    SpanishGuatemala = 4106,
    FrenchSwitzerland = 4108,
    LuleSamiNorway = 4155,
    CentralAtlasTamazightTifinaghMorocco = 4191,
    ArabicAlgeria = 5121,
    GermanLiechtenstein = 5127,
    EnglishNewZealand = 5129,
    SpanishCostaRica = 5130,
    FrenchLuxembourg = 5132,
    LuleSamiSweden = 5179,
    ArabicMorocco = 6145,
    EnglishIreland = 6153,
    SpanishPanama = 6154,
    FrenchMonaco = 6156,
    SerbianLatinBosniaHerzegovina = 6170,
    SouthernSamiNorway = 6203,
    ArabicTunisia = 7169,
    EnglishSouthAfrica = 7177,
    SpanishDominicanRepublic = 7178,
    FrenchCaribbean = 7180,
    SerbianCyrillicBosniaHerzegovina = 7194,
    SouthernSamiSweden = 7227,
    ArabicOman = 8193,
    EnglishJamaica = 8201,
    SpanishVenezuela = 8202,
    FrenchReunion = 8204,
    SkoltSamiFinland = 8251,
    ArabicYemen = 9217,
    EnglishCaribbean = 9225,
    SpanishColombia = 9226,
    FrenchCongoDRC = 9228,
    SerbianLatinSerbia = 9242,
    InariSamiFinland = 9275,
    ArabicSyria = 10241,
    EnglishBelize = 10249,
    SpanishPeru = 10250,
    FrenchSenegal = 10252,
    SerbianCyrillicSerbia = 10266,
    ArabicJordan = 11265,
    EnglishTrinidadTobago = 11273,
    SpanishArgentina = 11274,
    FrenchCameroon = 11276,
    SerbianLatinMontenegro = 11290,
    ArabicLebanon = 12289,
    EnglishZimbabwe = 12297,
    SpanishEcuador = 12298,
    FrenchCoteDIvoire = 12300,
    SerbianCyrillicMontenegro = 12314,
    ArabicKuwait = 13313,
    EnglishPhilippines = 13321,
    SpanishChile = 13322,
    FrenchMali = 13324,
    ArabicUnitedArabEmirates = 14337,
    EnglishIndonesia = 14345,
    SpanishUruguay = 14346,
    FrenchMorocco = 14348,
    ArabicBahrain = 15361,
    EnglishHongKongSAR = 15369,
    SpanishParaguay = 15370,
    FrenchHaiti = 15372,
    ArabicQatar = 16385,
    EnglishIndia = 16393,
    SpanishBolivia = 16394,
    EnglishMalaysia = 17417,
    SpanishElSalvador = 17418,
    EnglishSingapore = 18441,
    SpanishHonduras = 18442,
    SpanishNicaragua = 19466,
    SpanishPuertoRico = 20490,
    SpanishUnitedStates = 21514,
    SpanishLatinAmerica = 22538,
    SpanishCuba = 23562,

    // --- Script-neutral: a script with no region ---
    BosnianCyrillicNeutral = 25626,
    BosnianLatinNeutral = 26650,
    SerbianCyrillicNeutral = 27674,
    SerbianLatinNeutral = 28698,
    InariSamiNeutral = 28731,
    AzerbaijaniCyrillicNeutral = 29740,
    SkoltSamiNeutral = 29755,
    NorwegianNynorskNeutral = 30740,
    BosnianNeutral = 30746,
    AzerbaijaniLatinNeutral = 30764,
    SouthernSamiNeutral = 30779,
    UzbekCyrillicNeutral = 30787,
    CentralAtlasTamazightTifinaghNeutral = 30815,
    NorwegianBokmalNeutral = 31764,
    SerbianNeutral = 31770,
    LowerSorbianNeutral = 31790,
    LuleSamiNeutral = 31803,
    UzbekLatinNeutral = 31811,
    PunjabiArabicNeutral = 31814,
    MongolianMongolianNeutral = 31824,
    SindhiArabicNeutral = 31833,
    InuktitutLatinNeutral = 31837,
    FulaLatinNeutral = 31847,
}

/// <summary>
/// A fully-specified text collation: its <see cref="CollatingOrder"/> (LCID) plus the sort-order
/// <see cref="Version"/> that selects between weight tables sharing that LCID. Determines the index-key
/// bytes for text/memo columns, and is written into their column descriptors.
/// </summary>
/// <param name="Order">The collating order's LANGID — column descriptor <c>0x0B</c>/<c>0x0C</c>.</param>
/// <param name="Version">The sort-order version — the byte at column-descriptor <c>0x0E</c>. Verified vs
/// Access: the legacy compacted table (Access 2000–2007) = <c>0</c>; the "General" order Access 2010 made
/// default = <c>1</c>. Not a General-only axis: Croatian and Romanian each ship in both versions.</param>
/// <param name="SortId">The LCID's high word — column descriptor <c>0x0D</c>, page-0 <c>0x70</c>. Zero for a
/// locale's default order; non-zero selects a Windows <i>alternate sort order</i>, which is a different
/// ordering for the <i>same</i> LANGID. <b>Without it Hungarian Technical (<c>0x0001040E</c>) is
/// indistinguishable from Hungarian (<c>0x0000040E</c>), and German Phone Book from German.</b> This byte was
/// documented as "0 in every file seen — keep an eye on it" until fixtures for those two orders showed it
/// carrying <c>0x01</c>.</param>
public readonly record struct Collation(CollatingOrder Order, byte Version, byte SortId = 0)
{
    /// <summary>The full 32-bit Windows LCID: <c>(SortId &lt;&lt; 16) | LANGID</c>. The version is not part of
    /// it — Jet stores that in the LCID's unused top byte, but Windows does not define it.</summary>
    public int Lcid => (SortId << 16) | (int)Order;

    /// <summary>The sort-order version byte for the Access-2010+ "General" order.</summary>
    public const byte GeneralVersion = 1;

    /// <summary>Jet's "General legacy" order — locale 1033, version 0. The order LibRed reads and writes and
    /// can encode index keys for.</summary>
    public static Collation GeneralLegacy => new(CollatingOrder.General, 0);

    /// <summary>The Access-2010+ default "General" order — locale 1033, version 1. Its keys use the Windows
    /// NLS weights directly rather than General-Legacy's compacted table; see <c>JetTextCollationV1</c>.</summary>
    public static Collation General => new(CollatingOrder.General, GeneralVersion);

    /// <summary>Whether LibRed can encode index keys for this collation: both General orders (v0 via
    /// <c>JetTextCollation</c>, v1 via <c>JetTextCollationV1</c>), plus every locale with an entry in
    /// <c>JetLocaleTailoring</c>. Anything else is refused rather than encoded with the English table — see
    /// the format spec §10.4.</summary>
    public bool IsIndexKeyEncodable
    {
        get
        {
            if (this == GeneralLegacy || this == General) return true;
            // Both encoders tailor now. Version 1 was refused for a while on the grounds that its primaries
            // are two-byte NLS values "a different shape" — but that was a fact about the encoder, not about
            // the orders: Bosnian, Croatian and Serbian measure as General v1 plus twenty entries, the same
            // six devices every version-0 locale uses.
            return Storage.JetLocaleTailoring.For(this) is not null;
        }
    }
}
