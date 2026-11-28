# Załącznik A — pełna hierarchia i mapowanie FA(3)

Stan: 25.11.2026. Schemat: wzór 13775 z 25.06.2025, FA (3), 1-0E. Załącznik stanowi integralną część [raportu](../SALES_INVOICE_PREIMPLEMENTATION_ANALYSIS.md). Wygenerowany przez przejście skompilowanego XmlSchemaSet czterech oficjalnych XSD; bez połączeń z API KSeF.

## Zasady odczytu

Liczebność jest lokalna względem rodzica i grup. W CHOICE obowiązuje jedna gałąź na wystąpienie grupy; SEQUENCE wymaga wskazanej kolejności. Obowiązkowy liść wewnątrz opcjonalnego rodzica nie jest obowiązkowy globalnie. Drzewo pokazuje wszystkie grupy przodków, także opcjonalne i powtarzalne. Gdy grupa ma powtarzalność, powtarza się cała grupa. `*` oznacza unbounded. SQL/CLR ma stosować łącznie ograniczenia typu i jego baz z katalogu typów; inline podaje własne facety. `#text` oznacza treść simpleContent, a `@` atrybut.

Ścieżki modelu są normatywnymi nazwami pól proponowanych kontraktów; zachowanie polskich nazw w rozszerzeniach ogranicza ryzyko utraty informacji. Każdy powtarzalny węzeł w ścieżce tworzy uporządkowaną listę, również gdy zapis ścieżki potomka nie powtarza `[]`. Nullable wynika z nieobowiązkowości elementu/grupy, nie z typu CLR w tabeli. Stałe nagłówka nie wymagają osobnych kolumn: pozostają w niezmiennym XML artefaktu. Kontenery przechowujemy przez swoich potomków. Pola wynikowe są utrwalonym wynikiem wersjonowanego kalkulatora, bez edycji użytkownika. Pełne opisy semantyczne i warunki prawne należy czytać w oficjalnej broszurze i adnotacjach XSD; reguły między polami omawia raport.

## Hierarchia

```text
Faktura [1..1]
  (G1 SEQUENCE [1..1])
    Naglowek [1..1]
      (G2 SEQUENCE [1..1])
        KodFormularza [1..1]
          @kodSystemowy [Required] fixed=FA (3)
          @wersjaSchemy [Required] fixed=1-0E
        WariantFormularza [1..1]
        DataWytworzeniaFa [1..1]
        SystemInfo [0..1]
    Podmiot1 [1..1]
      (G3 SEQUENCE [1..1])
        PrefiksPodatnika [0..1]; fixed=PL
        NrEORI [0..1]
        DaneIdentyfikacyjne [1..1]
          (G4 SEQUENCE [1..1])
            NIP [1..1]
            Nazwa [1..1]
        Adres [1..1]
          (G5 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        AdresKoresp [0..1]
          (G6 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        DaneKontaktowe [0..3]
          (G7 SEQUENCE [0..1])
            Email [0..1]
            Telefon [0..1]
        StatusInfoPodatnika [0..1]
    Podmiot2 [1..1]
      (G8 SEQUENCE [1..1])
        NrEORI [0..1]
        DaneIdentyfikacyjne [1..1]
          (G9 SEQUENCE [1..1])
            (G10 CHOICE [1..1])
              NIP [1..1]
              (G11 SEQUENCE [1..1])
                KodUE [1..1]
                NrVatUE [1..1]
              (G12 SEQUENCE [1..1])
                KodKraju [0..1]
                NrID [1..1]
              BrakID [1..1]
            (G13 SEQUENCE [0..1])
              Nazwa [1..1]
        Adres [0..1]
          (G14 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        AdresKoresp [0..1]
          (G15 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        DaneKontaktowe [0..3]
          (G16 SEQUENCE [0..1])
            Email [0..1]
            Telefon [0..1]
        NrKlienta [0..1]
        IDNabywcy [0..1]
        JST [1..1]
        GV [1..1]
    Podmiot3 [0..100]
      (G17 SEQUENCE [1..1])
        IDNabywcy [0..1]
        NrEORI [0..1]
        DaneIdentyfikacyjne [1..1]
          (G18 SEQUENCE [1..1])
            (G19 CHOICE [1..1])
              NIP [1..1]
              IDWew [1..1]
              (G20 SEQUENCE [1..1])
                KodUE [1..1]
                NrVatUE [1..1]
              (G21 SEQUENCE [1..1])
                KodKraju [0..1]
                NrID [1..1]
              BrakID [1..1]
            (G22 SEQUENCE [0..1])
              Nazwa [1..1]
        Adres [0..1]
          (G23 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        AdresKoresp [0..1]
          (G24 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        DaneKontaktowe [0..3]
          (G25 SEQUENCE [1..1])
            Email [0..1]
            Telefon [0..1]
        (G26 CHOICE [1..1])
          Rola [1..1]
          (G27 SEQUENCE [1..1])
            RolaInna [1..1]
            OpisRoli [1..1]
        Udzial [0..1]
        NrKlienta [0..1]
    PodmiotUpowazniony [0..1]
      (G28 SEQUENCE [1..1])
        NrEORI [0..1]
        DaneIdentyfikacyjne [1..1]
          (G29 SEQUENCE [1..1])
            NIP [1..1]
            Nazwa [1..1]
        Adres [1..1]
          (G30 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        AdresKoresp [0..1]
          (G31 SEQUENCE [1..1])
            KodKraju [1..1]
            AdresL1 [1..1]
            AdresL2 [0..1]
            GLN [0..1]
        DaneKontaktowe [0..3]
          (G32 SEQUENCE [1..1])
            EmailPU [0..1]
            TelefonPU [0..1]
        RolaPU [1..1]
    Fa [1..1]
      (G33 SEQUENCE [1..1])
        KodWaluty [1..1]
        P_1 [1..1]
        P_1M [0..1]
        P_2 [1..1]
        WZ [0..1000]
        (G34 CHOICE [0..1])
          P_6 [1..1]
          OkresFa [1..1]
            (G35 SEQUENCE [1..1])
              P_6_Od [1..1]
              P_6_Do [1..1]
        (G36 SEQUENCE [0..1])
          P_13_1 [1..1]
          P_14_1 [1..1]
          P_14_1W [0..1]
        (G37 SEQUENCE [0..1])
          P_13_2 [1..1]
          P_14_2 [1..1]
          P_14_2W [0..1]
        (G38 SEQUENCE [0..1])
          P_13_3 [1..1]
          P_14_3 [1..1]
          P_14_3W [0..1]
        (G39 SEQUENCE [0..1])
          P_13_4 [1..1]
          P_14_4 [1..1]
          P_14_4W [0..1]
        (G40 SEQUENCE [0..1])
          P_13_5 [1..1]
          P_14_5 [0..1]
        P_13_6_1 [0..1]
        P_13_6_2 [0..1]
        P_13_6_3 [0..1]
        P_13_7 [0..1]
        P_13_8 [0..1]
        P_13_9 [0..1]
        P_13_10 [0..1]
        P_13_11 [0..1]
        P_15 [1..1]
        KursWalutyZ [0..1]
        Adnotacje [1..1]
          (G41 SEQUENCE [1..1])
            P_16 [1..1]
            P_17 [1..1]
            P_18 [1..1]
            P_18A [1..1]
            Zwolnienie [1..1]
              (G42 CHOICE [1..1])
                (G43 SEQUENCE [1..1])
                  P_19 [1..1]
                  (G44 CHOICE [1..1])
                    P_19A [1..1]
                    P_19B [1..1]
                    P_19C [1..1]
                P_19N [1..1]
            NoweSrodkiTransportu [1..1]
              (G45 CHOICE [1..1])
                (G46 SEQUENCE [1..1])
                  P_22 [1..1]
                  P_42_5 [1..1]
                  NowySrodekTransportu [1..10000]
                    (G47 SEQUENCE [1..1])
                      P_22A [1..1]
                      P_NrWierszaNST [1..1]
                      P_22BMK [0..1]
                      P_22BMD [0..1]
                      P_22BK [0..1]
                      P_22BNR [0..1]
                      P_22BRP [0..1]
                      (G48 CHOICE [1..1])
                        (G49 SEQUENCE [1..1])
                          P_22B [1..1]
                          (G50 CHOICE [0..1])
                            P_22B1 [0..1]
                            P_22B2 [0..1]
                            P_22B3 [0..1]
                            P_22B4 [0..1]
                          P_22BT [0..1]
                        (G51 SEQUENCE [1..1])
                          P_22C [1..1]
                          P_22C1 [0..1]
                        (G52 SEQUENCE [1..1])
                          P_22D [1..1]
                          P_22D1 [0..1]
                P_22N [1..1]
            P_23 [1..1]
            PMarzy [1..1]
              (G53 CHOICE [1..1])
                (G54 SEQUENCE [1..1])
                  P_PMarzy [1..1]
                  (G55 CHOICE [1..1])
                    P_PMarzy_2 [1..1]
                    P_PMarzy_3_1 [1..1]
                    P_PMarzy_3_2 [1..1]
                    P_PMarzy_3_3 [1..1]
                P_PMarzyN [1..1]
        RodzajFaktury [1..1]
        (G56 SEQUENCE [0..1])
          PrzyczynaKorekty [0..1]
          TypKorekty [0..1]
          DaneFaKorygowanej [1..50000]
            (G57 SEQUENCE [1..1])
              DataWystFaKorygowanej [1..1]
              NrFaKorygowanej [1..1]
              (G58 CHOICE [1..1])
                (G59 SEQUENCE [1..1])
                  NrKSeF [1..1]
                  NrKSeFFaKorygowanej [1..1]
                NrKSeFN [1..1]
          OkresFaKorygowanej [0..1]
          NrFaKorygowany [0..1]
          Podmiot1K [0..1]
            (G60 SEQUENCE [1..1])
              PrefiksPodatnika [0..1]
              DaneIdentyfikacyjne [1..1]
                (G61 SEQUENCE [1..1])
                  NIP [1..1]
                  Nazwa [1..1]
              Adres [1..1]
                (G62 SEQUENCE [1..1])
                  KodKraju [1..1]
                  AdresL1 [1..1]
                  AdresL2 [0..1]
                  GLN [0..1]
          Podmiot2K [0..101]
            (G63 SEQUENCE [1..1])
              DaneIdentyfikacyjne [1..1]
                (G64 SEQUENCE [1..1])
                  (G65 CHOICE [1..1])
                    NIP [1..1]
                    (G66 SEQUENCE [1..1])
                      KodUE [1..1]
                      NrVatUE [1..1]
                    (G67 SEQUENCE [1..1])
                      KodKraju [0..1]
                      NrID [1..1]
                    BrakID [1..1]
                  (G68 SEQUENCE [0..1])
                    Nazwa [1..1]
              Adres [0..1]
                (G69 SEQUENCE [1..1])
                  KodKraju [1..1]
                  AdresL1 [1..1]
                  AdresL2 [0..1]
                  GLN [0..1]
              IDNabywcy [0..1]
          (G70 SEQUENCE [0..1])
            P_15ZK [1..1]
            KursWalutyZK [0..1]
        ZaliczkaCzesciowa [0..31]
          (G71 SEQUENCE [1..1])
            P_6Z [1..1]
            P_15Z [1..1]
            KursWalutyZW [0..1]
        FP [0..1]
        TP [0..1]
        DodatkowyOpis [0..10000]
          (G72 SEQUENCE [1..1])
            NrWiersza [0..1]
            Klucz [1..1]
            Wartosc [1..1]
        FakturaZaliczkowa [0..100]
          (G73 CHOICE [1..1])
            (G74 SEQUENCE [1..1])
              NrKSeFZN [1..1]
              NrFaZaliczkowej [1..1]
            NrKSeFFaZaliczkowej [1..1]
        ZwrotAkcyzy [0..1]
        FaWiersz [0..10000]
          (G75 SEQUENCE [1..1])
            NrWierszaFa [1..1]
            UU_ID [0..1]
            P_6A [0..1]
            P_7 [0..1]
            Indeks [0..1]
            GTIN [0..1]
            PKWiU [0..1]
            CN [0..1]
            PKOB [0..1]
            P_8A [0..1]
            P_8B [0..1]
            P_9A [0..1]
            P_9B [0..1]
            P_10 [0..1]
            P_11 [0..1]
            P_11A [0..1]
            P_11Vat [0..1]
            P_12 [0..1]
            P_12_XII [0..1]
            P_12_Zal_15 [0..1]
            KwotaAkcyzy [0..1]
            GTU [0..1]
            Procedura [0..1]
            KursWaluty [0..1]
            StanPrzed [0..1]
        Rozliczenie [0..1]
          (G76 SEQUENCE [1..1])
            Obciazenia [0..100]
              (G77 SEQUENCE [1..1])
                Kwota [1..1]
                Powod [1..1]
            SumaObciazen [0..1]
            Odliczenia [0..100]
              (G78 SEQUENCE [1..1])
                Kwota [1..1]
                Powod [1..1]
            SumaOdliczen [0..1]
            (G79 CHOICE [0..1])
              DoZaplaty [1..1]
              DoRozliczenia [1..1]
        Platnosc [0..1]
          (G80 SEQUENCE [1..1])
            (G81 CHOICE [0..1])
              (G82 SEQUENCE [1..1])
                Zaplacono [1..1]
                DataZaplaty [1..1]
              (G83 SEQUENCE [1..1])
                ZnacznikZaplatyCzesciowej [1..1]
                ZaplataCzesciowa [1..100]
                  (G84 SEQUENCE [1..1])
                    KwotaZaplatyCzesciowej [1..1]
                    DataZaplatyCzesciowej [1..1]
                    (G85 CHOICE [0..1])
                      FormaPlatnosci [1..1]
                      (G86 SEQUENCE [1..1])
                        PlatnoscInna [1..1]
                        OpisPlatnosci [1..1]
            TerminPlatnosci [0..100]
              (G87 SEQUENCE [0..1])
                Termin [0..1]
                TerminOpis [0..1]
                  (G88 SEQUENCE [1..1])
                    Ilosc [1..1]
                    Jednostka [1..1]
                    ZdarzeniePoczatkowe [1..1]
            (G89 CHOICE [0..1])
              FormaPlatnosci [1..1]
              (G90 SEQUENCE [1..1])
                PlatnoscInna [1..1]
                OpisPlatnosci [1..1]
            RachunekBankowy [0..100]
              (G91 SEQUENCE [1..1])
                NrRB [1..1]
                SWIFT [0..1]
                RachunekWlasnyBanku [0..1]
                NazwaBanku [0..1]
                OpisRachunku [0..1]
            RachunekBankowyFaktora [0..20]
              (G92 SEQUENCE [1..1])
                NrRB [1..1]
                SWIFT [0..1]
                RachunekWlasnyBanku [0..1]
                NazwaBanku [0..1]
                OpisRachunku [0..1]
            Skonto [0..1]
              (G93 SEQUENCE [1..1])
                WarunkiSkonta [1..1]
                WysokoscSkonta [1..1]
            LinkDoPlatnosci [0..1]
            IPKSeF [0..1]
        WarunkiTransakcji [0..1]
          (G94 SEQUENCE [1..1])
            Umowy [0..100]
              (G95 SEQUENCE [0..1])
                DataUmowy [0..1]
                NrUmowy [0..1]
            Zamowienia [0..100]
              (G96 SEQUENCE [0..1])
                DataZamowienia [0..1]
                NrZamowienia [0..1]
            NrPartiiTowaru [0..1000]
            WarunkiDostawy [0..1]
            (G97 SEQUENCE [0..1])
              KursUmowny [1..1]
              WalutaUmowna [1..1]
            Transport [0..20]
              (G98 SEQUENCE [1..1])
                (G99 CHOICE [1..1])
                  RodzajTransportu [1..1]
                  (G100 SEQUENCE [1..1])
                    TransportInny [1..1]
                    OpisInnegoTransportu [1..1]
                Przewoznik [0..1]
                  (G101 SEQUENCE [1..1])
                    DaneIdentyfikacyjne [1..1]
                      (G102 SEQUENCE [1..1])
                        (G103 CHOICE [1..1])
                          NIP [1..1]
                          (G104 SEQUENCE [1..1])
                            KodUE [1..1]
                            NrVatUE [1..1]
                          (G105 SEQUENCE [1..1])
                            KodKraju [0..1]
                            NrID [1..1]
                          BrakID [1..1]
                        (G106 SEQUENCE [0..1])
                          Nazwa [1..1]
                    AdresPrzewoznika [1..1]
                      (G107 SEQUENCE [1..1])
                        KodKraju [1..1]
                        AdresL1 [1..1]
                        AdresL2 [0..1]
                        GLN [0..1]
                NrZleceniaTransportu [0..1]
                (G108 CHOICE [1..1])
                  OpisLadunku [1..1]
                  (G109 SEQUENCE [1..1])
                    LadunekInny [1..1]
                    OpisInnegoLadunku [1..1]
                JednostkaOpakowania [0..1]
                (G110 SEQUENCE [0..1])
                  DataGodzRozpTransportu [0..1]
                  DataGodzZakTransportu [0..1]
                  WysylkaZ [0..1]
                    (G111 SEQUENCE [1..1])
                      KodKraju [1..1]
                      AdresL1 [1..1]
                      AdresL2 [0..1]
                      GLN [0..1]
                  WysylkaPrzez [0..20]
                    (G112 SEQUENCE [1..1])
                      KodKraju [1..1]
                      AdresL1 [1..1]
                      AdresL2 [0..1]
                      GLN [0..1]
                  WysylkaDo [0..1]
                    (G113 SEQUENCE [1..1])
                      KodKraju [1..1]
                      AdresL1 [1..1]
                      AdresL2 [0..1]
                      GLN [0..1]
            PodmiotPosredniczacy [0..1]
        Zamowienie [0..1]
          (G114 SEQUENCE [1..1])
            WartoscZamowienia [1..1]
            ZamowienieWiersz [1..10000]
              (G115 SEQUENCE [1..1])
                NrWierszaZam [1..1]
                UU_IDZ [0..1]
                P_7Z [0..1]
                IndeksZ [0..1]
                GTINZ [0..1]
                PKWiUZ [0..1]
                CNZ [0..1]
                PKOBZ [0..1]
                P_8AZ [0..1]
                P_8BZ [0..1]
                P_9AZ [0..1]
                P_11NettoZ [0..1]
                P_11VatZ [0..1]
                P_12Z [0..1]
                P_12Z_XII [0..1]
                P_12Z_Zal_15 [0..1]
                GTUZ [0..1]
                ProceduraZ [0..1]
                KwotaAkcyzyZ [0..1]
                StanPrzedZ [0..1]
    Stopka [0..1]
      (G116 SEQUENCE [0..1])
        Informacje [0..3]
          (G117 SEQUENCE [0..1])
            StopkaFaktury [0..1]
        Rejestry [0..100]
          (G118 SEQUENCE [0..1])
            PelnaNazwa [0..1]
            KRS [0..1]
            REGON [0..1]
            BDO [0..1]
    Zalacznik [0..1]
      (G119 SEQUENCE [1..1])
        BlokDanych [1..1000]
          (G120 SEQUENCE [1..1])
            ZNaglowek [0..1]
            MetaDane [1..1000]
              (G121 SEQUENCE [1..1])
                ZKlucz [1..1]
                ZWartosc [1..1]
            Tekst [0..1]
              (G122 SEQUENCE [1..1])
                Akapit [1..10]
            (G123 SEQUENCE [0..1])
              Tabela [0..1000]
                (G124 SEQUENCE [1..1])
                  TMetaDane [0..1000]
                    (G125 SEQUENCE [1..1])
                      TKlucz [1..1]
                      TWartosc [1..1]
                  Opis [0..1]
                  TNaglowek [1..1]
                    (G126 SEQUENCE [1..1])
                      Kol [1..20]
                        @Typ [Required] fixed=
                        (G127 SEQUENCE [1..1])
                          NKom [1..1]
                  Wiersz [1..1000]
                    (G128 SEQUENCE [1..1])
                      WKom [1..20]
                  Suma [0..1]
                    (G129 SEQUENCE [1..1])
                      SKom [1..20]
```

## Wszystkie elementy i atrybuty — przypisanie do modelu

| Ścieżka XML | Lokalnie | Grupy bezpośrednie | Typ / facety | CLR | Miejsce / sposób utrwalenia |
|---|---|---|---|---|---|
| `/Faktura` | 1..1 |  | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice (agregat)` |
| `/Faktura/Naglowek` | 1..1 | G1 SEQUENCE[1..1] | tns:TNaglowek | obiekt / kolekcja; patrz potomkowie | `SubmissionArtifact.Header (stałe i czas generowania; utrwalić w XML)` |
| `/Faktura/Naglowek/KodFormularza` | 1..1 | G2 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SubmissionArtifact.Header.KodFormularza (stałe i czas generowania; utrwalić w XML)` |
| `/Faktura/Naglowek/KodFormularza/@kodSystemowy` | 1..1 | atrybut | xs:string [base=xs:anySimpleType; ]; fixed=FA (3) | string | `SubmissionArtifact.Header.KodFormularza (stałe i czas generowania; utrwalić w XML).@kodSystemowy` |
| `/Faktura/Naglowek/KodFormularza/@wersjaSchemy` | 1..1 | atrybut | xs:string [base=xs:anySimpleType; ]; fixed=1-0E | string | `SubmissionArtifact.Header.KodFormularza (stałe i czas generowania; utrwalić w XML).@wersjaSchemy` |
| `/Faktura/Naglowek/KodFormularza/#text` | 1..1 | simpleContent | tns:TKodFormularza [base=xs:string; Enumeration=FA] | string | `SubmissionArtifact.Header.KodFormularza (stałe i czas generowania; utrwalić w XML).Value` |
| `/Faktura/Naglowek/WariantFormularza` | 1..1 | G2 SEQUENCE[1..1] | inline [base=xs:byte; Enumeration=3] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SubmissionArtifact.Header.WariantFormularza (stałe i czas generowania; utrwalić w XML)` |
| `/Faktura/Naglowek/DataWytworzeniaFa` | 1..1 | G2 SEQUENCE[1..1] | inline [base=etd:TDataCzas; MinInclusive=2025-09-01T00:00:00Z; MaxInclusive=2050-01-01T23:59:59Z] | DateTimeOffset | `SubmissionArtifact.Header.DataWytworzeniaFa (stałe i czas generowania; utrwalić w XML)` |
| `/Faktura/Naglowek/SystemInfo` | 0..1 | G2 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SubmissionArtifact.Header.SystemInfo (stałe i czas generowania; utrwalić w XML)` |
| `/Faktura/Podmiot1` | 1..1 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Seller` |
| `/Faktura/Podmiot1/PrefiksPodatnika` | 0..1 | G3 SEQUENCE[1..1] | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI]; fixed=PL | string (kody: enum/value object) | `SalesInvoice.Seller.PrefiksPodatnika` |
| `/Faktura/Podmiot1/NrEORI` | 0..1 | G3 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Seller.NrEORI` |
| `/Faktura/Podmiot1/DaneIdentyfikacyjne` | 1..1 | G3 SEQUENCE[1..1] | tns:TPodmiot1 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Seller.DaneIdentyfikacyjne` |
| `/Faktura/Podmiot1/DaneIdentyfikacyjne/NIP` | 1..1 | G4 SEQUENCE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.Seller.DaneIdentyfikacyjne.NIP` |
| `/Faktura/Podmiot1/DaneIdentyfikacyjne/Nazwa` | 1..1 | G4 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Seller.DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/Podmiot1/Adres` | 1..1 | G3 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Seller.Adres` |
| `/Faktura/Podmiot1/Adres/KodKraju` | 1..1 | G5 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Seller.Adres.KodKraju` |
| `/Faktura/Podmiot1/Adres/AdresL1` | 1..1 | G5 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Seller.Adres.AdresL1` |
| `/Faktura/Podmiot1/Adres/AdresL2` | 0..1 | G5 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Seller.Adres.AdresL2` |
| `/Faktura/Podmiot1/Adres/GLN` | 0..1 | G5 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.Seller.Adres.GLN` |
| `/Faktura/Podmiot1/AdresKoresp` | 0..1 | G3 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Seller.AdresKoresp` |
| `/Faktura/Podmiot1/AdresKoresp/KodKraju` | 1..1 | G6 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Seller.AdresKoresp.KodKraju` |
| `/Faktura/Podmiot1/AdresKoresp/AdresL1` | 1..1 | G6 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Seller.AdresKoresp.AdresL1` |
| `/Faktura/Podmiot1/AdresKoresp/AdresL2` | 0..1 | G6 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Seller.AdresKoresp.AdresL2` |
| `/Faktura/Podmiot1/AdresKoresp/GLN` | 0..1 | G6 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.Seller.AdresKoresp.GLN` |
| `/Faktura/Podmiot1/DaneKontaktowe` | 0..3 | G3 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Seller.DaneKontaktowe` |
| `/Faktura/Podmiot1/DaneKontaktowe/Email` | 0..1 | G7 SEQUENCE[0..1] | etd:TAdresEmail [base=xs:token; MinLength=3; MaxLength=255; Pattern=(.)+@(.)+] | string (kody: enum/value object) | `SalesInvoice.Seller.DaneKontaktowe.Email` |
| `/Faktura/Podmiot1/DaneKontaktowe/Telefon` | 0..1 | G7 SEQUENCE[0..1] | tns:TNumerTelefonu [base=tns:TZnakowy; MaxLength=16] | string (kody: enum/value object) | `SalesInvoice.Seller.DaneKontaktowe.Telefon` |
| `/Faktura/Podmiot1/StatusInfoPodatnika` | 0..1 | G3 SEQUENCE[1..1] | tns:TStatusInfoPodatnika [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Seller.StatusInfoPodatnika` |
| `/Faktura/Podmiot2` | 1..1 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Buyer` |
| `/Faktura/Podmiot2/NrEORI` | 0..1 | G8 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Buyer.NrEORI` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne` | 1..1 | G8 SEQUENCE[1..1] | tns:TPodmiot2 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Buyer.DaneIdentyfikacyjne` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/NIP` | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.NIP` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/KodUE` | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] / G11 SEQUENCE[1..1] | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.KodUE` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/NrVatUE` | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] / G11 SEQUENCE[1..1] | tns:TNrVatUE [base=xs:string; Pattern=(\d&#124;[A-Z]&#124;\+&#124;\*){1,12}] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.NrVatUE` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/KodKraju` | 0..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] / G12 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.KodKraju` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/NrID` | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] / G12 SEQUENCE[1..1] | inline [base=etd:TNrIdentyfikacjiPodatkowej; ] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.NrID` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/BrakID` | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.BrakID` |
| `/Faktura/Podmiot2/DaneIdentyfikacyjne/Nazwa` | 1..1 | G9 SEQUENCE[1..1] / G13 SEQUENCE[0..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/Podmiot2/Adres` | 0..1 | G8 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Buyer.Adres` |
| `/Faktura/Podmiot2/Adres/KodKraju` | 1..1 | G14 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Buyer.Adres.KodKraju` |
| `/Faktura/Podmiot2/Adres/AdresL1` | 1..1 | G14 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Buyer.Adres.AdresL1` |
| `/Faktura/Podmiot2/Adres/AdresL2` | 0..1 | G14 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Buyer.Adres.AdresL2` |
| `/Faktura/Podmiot2/Adres/GLN` | 0..1 | G14 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.Buyer.Adres.GLN` |
| `/Faktura/Podmiot2/AdresKoresp` | 0..1 | G8 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Buyer.AdresKoresp` |
| `/Faktura/Podmiot2/AdresKoresp/KodKraju` | 1..1 | G15 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Buyer.AdresKoresp.KodKraju` |
| `/Faktura/Podmiot2/AdresKoresp/AdresL1` | 1..1 | G15 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Buyer.AdresKoresp.AdresL1` |
| `/Faktura/Podmiot2/AdresKoresp/AdresL2` | 0..1 | G15 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Buyer.AdresKoresp.AdresL2` |
| `/Faktura/Podmiot2/AdresKoresp/GLN` | 0..1 | G15 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.Buyer.AdresKoresp.GLN` |
| `/Faktura/Podmiot2/DaneKontaktowe` | 0..3 | G8 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Buyer.DaneKontaktowe` |
| `/Faktura/Podmiot2/DaneKontaktowe/Email` | 0..1 | G16 SEQUENCE[0..1] | etd:TAdresEmail [base=xs:token; MinLength=3; MaxLength=255; Pattern=(.)+@(.)+] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneKontaktowe.Email` |
| `/Faktura/Podmiot2/DaneKontaktowe/Telefon` | 0..1 | G16 SEQUENCE[0..1] | tns:TNumerTelefonu [base=tns:TZnakowy; MaxLength=16] | string (kody: enum/value object) | `SalesInvoice.Buyer.DaneKontaktowe.Telefon` |
| `/Faktura/Podmiot2/NrKlienta` | 0..1 | G8 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Buyer.NrKlienta` |
| `/Faktura/Podmiot2/IDNabywcy` | 0..1 | G8 SEQUENCE[1..1] | inline [base=tns:TZnakowy50; MaxLength=32] | string (kody: enum/value object) | `SalesInvoice.Buyer.IDNabywcy` |
| `/Faktura/Podmiot2/JST` | 1..1 | G8 SEQUENCE[1..1] | inline [base=xs:integer; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Buyer.JST` |
| `/Faktura/Podmiot2/GV` | 1..1 | G8 SEQUENCE[1..1] | inline [base=xs:integer; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Buyer.GV` |
| `/Faktura/Podmiot3` | 0..100 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.ThirdParties[]` |
| `/Faktura/Podmiot3/IDNabywcy` | 0..1 | G17 SEQUENCE[1..1] | inline [base=tns:TZnakowy50; MaxLength=32] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].IDNabywcy` |
| `/Faktura/Podmiot3/NrEORI` | 0..1 | G17 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].NrEORI` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne` | 1..1 | G17 SEQUENCE[1..1] | tns:TPodmiot3 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/NIP` | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.NIP` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/IDWew` | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] | tns:TNIPIdWew [base=tns:TZnakowy20; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}-\d{5}] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.IDWew` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/KodUE` | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] / G20 SEQUENCE[1..1] | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.KodUE` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/NrVatUE` | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] / G20 SEQUENCE[1..1] | tns:TNrVatUE [base=xs:string; Pattern=(\d&#124;[A-Z]&#124;\+&#124;\*){1,12}] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.NrVatUE` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/KodKraju` | 0..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] / G21 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.KodKraju` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/NrID` | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] / G21 SEQUENCE[1..1] | inline [base=etd:TNrIdentyfikacjiPodatkowej; ] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.NrID` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/BrakID` | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.BrakID` |
| `/Faktura/Podmiot3/DaneIdentyfikacyjne/Nazwa` | 1..1 | G18 SEQUENCE[1..1] / G22 SEQUENCE[0..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/Podmiot3/Adres` | 0..1 | G17 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.ThirdParties[].Adres` |
| `/Faktura/Podmiot3/Adres/KodKraju` | 1..1 | G23 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].Adres.KodKraju` |
| `/Faktura/Podmiot3/Adres/AdresL1` | 1..1 | G23 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].Adres.AdresL1` |
| `/Faktura/Podmiot3/Adres/AdresL2` | 0..1 | G23 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].Adres.AdresL2` |
| `/Faktura/Podmiot3/Adres/GLN` | 0..1 | G23 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].Adres.GLN` |
| `/Faktura/Podmiot3/AdresKoresp` | 0..1 | G17 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.ThirdParties[].AdresKoresp` |
| `/Faktura/Podmiot3/AdresKoresp/KodKraju` | 1..1 | G24 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].AdresKoresp.KodKraju` |
| `/Faktura/Podmiot3/AdresKoresp/AdresL1` | 1..1 | G24 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].AdresKoresp.AdresL1` |
| `/Faktura/Podmiot3/AdresKoresp/AdresL2` | 0..1 | G24 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].AdresKoresp.AdresL2` |
| `/Faktura/Podmiot3/AdresKoresp/GLN` | 0..1 | G24 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].AdresKoresp.GLN` |
| `/Faktura/Podmiot3/DaneKontaktowe` | 0..3 | G17 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.ThirdParties[].DaneKontaktowe` |
| `/Faktura/Podmiot3/DaneKontaktowe/Email` | 0..1 | G25 SEQUENCE[1..1] | etd:TAdresEmail [base=xs:token; MinLength=3; MaxLength=255; Pattern=(.)+@(.)+] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneKontaktowe.Email` |
| `/Faktura/Podmiot3/DaneKontaktowe/Telefon` | 0..1 | G25 SEQUENCE[1..1] | tns:TNumerTelefonu [base=tns:TZnakowy; MaxLength=16] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].DaneKontaktowe.Telefon` |
| `/Faktura/Podmiot3/Rola` | 1..1 | G17 SEQUENCE[1..1] / G26 CHOICE[1..1] | tns:TRolaPodmiotu3 [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7; Enumeration=8; Enumeration=9; Enumeration=10; Enumeration=11] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.ThirdParties[].Rola` |
| `/Faktura/Podmiot3/RolaInna` | 1..1 | G17 SEQUENCE[1..1] / G26 CHOICE[1..1] / G27 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.ThirdParties[].RolaInna` |
| `/Faktura/Podmiot3/OpisRoli` | 1..1 | G17 SEQUENCE[1..1] / G26 CHOICE[1..1] / G27 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].OpisRoli` |
| `/Faktura/Podmiot3/Udzial` | 0..1 | G17 SEQUENCE[1..1] | tns:TProcentowy [base=xs:decimal; TotalDigits=9; FractionDigits=6; MinInclusive=0; MaxInclusive=100; WhiteSpace=collapse] | decimal | `SalesInvoice.ThirdParties[].Udzial` |
| `/Faktura/Podmiot3/NrKlienta` | 0..1 | G17 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.ThirdParties[].NrKlienta` |
| `/Faktura/PodmiotUpowazniony` | 0..1 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AuthorizedParty` |
| `/Faktura/PodmiotUpowazniony/NrEORI` | 0..1 | G28 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.NrEORI` |
| `/Faktura/PodmiotUpowazniony/DaneIdentyfikacyjne` | 1..1 | G28 SEQUENCE[1..1] | tns:TPodmiot1 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AuthorizedParty.DaneIdentyfikacyjne` |
| `/Faktura/PodmiotUpowazniony/DaneIdentyfikacyjne/NIP` | 1..1 | G29 SEQUENCE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.DaneIdentyfikacyjne.NIP` |
| `/Faktura/PodmiotUpowazniony/DaneIdentyfikacyjne/Nazwa` | 1..1 | G29 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/PodmiotUpowazniony/Adres` | 1..1 | G28 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AuthorizedParty.Adres` |
| `/Faktura/PodmiotUpowazniony/Adres/KodKraju` | 1..1 | G30 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.Adres.KodKraju` |
| `/Faktura/PodmiotUpowazniony/Adres/AdresL1` | 1..1 | G30 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.Adres.AdresL1` |
| `/Faktura/PodmiotUpowazniony/Adres/AdresL2` | 0..1 | G30 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.Adres.AdresL2` |
| `/Faktura/PodmiotUpowazniony/Adres/GLN` | 0..1 | G30 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.Adres.GLN` |
| `/Faktura/PodmiotUpowazniony/AdresKoresp` | 0..1 | G28 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AuthorizedParty.AdresKoresp` |
| `/Faktura/PodmiotUpowazniony/AdresKoresp/KodKraju` | 1..1 | G31 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.AdresKoresp.KodKraju` |
| `/Faktura/PodmiotUpowazniony/AdresKoresp/AdresL1` | 1..1 | G31 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.AdresKoresp.AdresL1` |
| `/Faktura/PodmiotUpowazniony/AdresKoresp/AdresL2` | 0..1 | G31 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.AdresKoresp.AdresL2` |
| `/Faktura/PodmiotUpowazniony/AdresKoresp/GLN` | 0..1 | G31 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.AdresKoresp.GLN` |
| `/Faktura/PodmiotUpowazniony/DaneKontaktowe` | 0..3 | G28 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AuthorizedParty.DaneKontaktowe` |
| `/Faktura/PodmiotUpowazniony/DaneKontaktowe/EmailPU` | 0..1 | G32 SEQUENCE[1..1] | etd:TAdresEmail [base=xs:token; MinLength=3; MaxLength=255; Pattern=(.)+@(.)+] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.DaneKontaktowe.EmailPU` |
| `/Faktura/PodmiotUpowazniony/DaneKontaktowe/TelefonPU` | 0..1 | G32 SEQUENCE[1..1] | tns:TNumerTelefonu [base=tns:TZnakowy; MaxLength=16] | string (kody: enum/value object) | `SalesInvoice.AuthorizedParty.DaneKontaktowe.TelefonPU` |
| `/Faktura/PodmiotUpowazniony/RolaPU` | 1..1 | G28 SEQUENCE[1..1] | tns:TRolaPodmiotuUpowaznionego [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.AuthorizedParty.RolaPU` |
| `/Faktura/Fa` | 1..1 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice (dane Fa)` |
| `/Faktura/Fa/KodWaluty` | 1..1 | G33 SEQUENCE[1..1] | tns:TKodWaluty [base=xs:normalizedString; Enumeration=AED; Enumeration=AFN; Enumeration=ALL; Enumeration=AMD; Enumeration=ANG; Enumeration=AOA; Enumeration=ARS; Enumeration=AUD; Enumeration=AWG; Enumeration=AZN; Enumeration=BAM; Enumeration=BBD; Enumeration=BDT; Enumeration=BGN; Enumeration=BHD; Enumeration=BIF; Enumeration=BMD; Enumeration=BND; Enumeration=BOB; Enumeration=BOV; Enumeration=BRL; Enumeration=BSD; Enumeration=BTN; Enumeration=BWP; Enumeration=BYN; Enumeration=BZD; Enumeration=CAD; Enumeration=CDF; Enumeration=CHE; Enumeration=CHF; Enumeration=CHW; Enumeration=CLF; Enumeration=CLP; Enumeration=CNY; Enumeration=COP; Enumeration=COU; Enumeration=CRC; Enumeration=CUC; Enumeration=CUP; Enumeration=CVE; Enumeration=CZK; Enumeration=DJF; Enumeration=DKK; Enumeration=DOP; Enumeration=DZD; Enumeration=EGP; Enumeration=ERN; Enumeration=ETB; Enumeration=EUR; Enumeration=FJD; Enumeration=FKP; Enumeration=GBP; Enumeration=GEL; Enumeration=GGP; Enumeration=GHS; Enumeration=GIP; Enumeration=GMD; Enumeration=GNF; Enumeration=GTQ; Enumeration=GYD; Enumeration=HKD; Enumeration=HNL; Enumeration=HRK; Enumeration=HTG; Enumeration=HUF; Enumeration=IDR; Enumeration=ILS; Enumeration=IMP; Enumeration=INR; Enumeration=IQD; Enumeration=IRR; Enumeration=ISK; Enumeration=JEP; Enumeration=JMD; Enumeration=JOD; Enumeration=JPY; Enumeration=KES; Enumeration=KGS; Enumeration=KHR; Enumeration=KMF; Enumeration=KPW; Enumeration=KRW; Enumeration=KWD; Enumeration=KYD; Enumeration=KZT; Enumeration=LAK; Enumeration=LBP; Enumeration=LKR; Enumeration=LRD; Enumeration=LSL; Enumeration=LYD; Enumeration=MAD; Enumeration=MDL; Enumeration=MGA; Enumeration=MKD; Enumeration=MMK; Enumeration=MNT; Enumeration=MOP; Enumeration=MRU; Enumeration=MUR; Enumeration=MVR; Enumeration=MWK; Enumeration=MXN; Enumeration=MXV; Enumeration=MYR; Enumeration=MZN; Enumeration=NAD; Enumeration=NGN; Enumeration=NIO; Enumeration=NOK; Enumeration=NPR; Enumeration=NZD; Enumeration=OMR; Enumeration=PAB; Enumeration=PEN; Enumeration=PGK; Enumeration=PHP; Enumeration=PKR; Enumeration=PLN; Enumeration=PYG; Enumeration=QAR; Enumeration=RON; Enumeration=RSD; Enumeration=RUB; Enumeration=RWF; Enumeration=SAR; Enumeration=SBD; Enumeration=SCR; Enumeration=SDG; Enumeration=SEK; Enumeration=SGD; Enumeration=SHP; Enumeration=SLL; Enumeration=SOS; Enumeration=SRD; Enumeration=SSP; Enumeration=STN; Enumeration=SVC; Enumeration=SYP; Enumeration=SZL; Enumeration=THB; Enumeration=TJS; Enumeration=TMT; Enumeration=TND; Enumeration=TOP; Enumeration=TRY; Enumeration=TTD; Enumeration=TWD; Enumeration=TZS; Enumeration=UAH; Enumeration=UGX; Enumeration=USD; Enumeration=USN; Enumeration=UYI; Enumeration=UYU; Enumeration=UYW; Enumeration=UZS; Enumeration=VES; Enumeration=VND; Enumeration=VUV; Enumeration=WST; Enumeration=XAF; Enumeration=XAG; Enumeration=XAU; Enumeration=XBA; Enumeration=XBB; Enumeration=XBC; Enumeration=XBD; Enumeration=XCD; Enumeration=XCG; Enumeration=XDR; Enumeration=XOF; Enumeration=XPD; Enumeration=XPF; Enumeration=XPT; Enumeration=XSU; Enumeration=XUA; Enumeration=XXX; Enumeration=YER; Enumeration=ZAR; Enumeration=ZMW; Enumeration=ZWL] | string (kody: enum/value object) | `SalesInvoice.Currency` |
| `/Faktura/Fa/P_1` | 1..1 | G33 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.PlannedIssueDate / IssueDate` |
| `/Faktura/Fa/P_1M` | 0..1 | G33 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.IssuePlace` |
| `/Faktura/Fa/P_2` | 1..1 | G33 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.InvoiceNumber` |
| `/Faktura/Fa/WZ` | 0..1000 | G33 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.FaDetails.WZ` |
| `/Faktura/Fa/P_6` | 1..1 | G33 SEQUENCE[1..1] / G34 CHOICE[0..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.FaDetails.P_6` |
| `/Faktura/Fa/OkresFa` | 1..1 | G33 SEQUENCE[1..1] / G34 CHOICE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.FaDetails.OkresFa` |
| `/Faktura/Fa/OkresFa/P_6_Od` | 1..1 | G35 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.FaDetails.OkresFa.P_6_Od` |
| `/Faktura/Fa/OkresFa/P_6_Do` | 1..1 | G35 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.FaDetails.OkresFa.P_6_Do` |
| `/Faktura/Fa/P_13_1` | 1..1 | G33 SEQUENCE[1..1] / G36 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_1 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_1` | 1..1 | G33 SEQUENCE[1..1] / G36 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_1 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_1W` | 0..1 | G33 SEQUENCE[1..1] / G36 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_1W (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_2` | 1..1 | G33 SEQUENCE[1..1] / G37 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_2 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_2` | 1..1 | G33 SEQUENCE[1..1] / G37 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_2 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_2W` | 0..1 | G33 SEQUENCE[1..1] / G37 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_2W (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_3` | 1..1 | G33 SEQUENCE[1..1] / G38 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_3 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_3` | 1..1 | G33 SEQUENCE[1..1] / G38 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_3 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_3W` | 0..1 | G33 SEQUENCE[1..1] / G38 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_3W (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_4` | 1..1 | G33 SEQUENCE[1..1] / G39 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_4 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_4` | 1..1 | G33 SEQUENCE[1..1] / G39 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_4 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_4W` | 0..1 | G33 SEQUENCE[1..1] / G39 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_4W (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_5` | 1..1 | G33 SEQUENCE[1..1] / G40 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_5 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_14_5` | 0..1 | G33 SEQUENCE[1..1] / G40 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_14_5 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_6_1` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_6_1 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_6_2` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_6_2 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_6_3` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_6_3 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_7` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_7 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_8` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_8 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_9` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_9 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_10` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_10 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_13_11` | 0..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_13_11 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/P_15` | 1..1 | G33 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Totals.P_15 (wynik kalkulacji; utrwalić)` |
| `/Faktura/Fa/KursWalutyZ` | 0..1 | G33 SEQUENCE[1..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.FaDetails.KursWalutyZ` |
| `/Faktura/Fa/Adnotacje` | 1..1 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Annotations` |
| `/Faktura/Fa/Adnotacje/P_16` | 1..1 | G41 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.P_16` |
| `/Faktura/Fa/Adnotacje/P_17` | 1..1 | G41 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.P_17` |
| `/Faktura/Fa/Adnotacje/P_18` | 1..1 | G41 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.P_18` |
| `/Faktura/Fa/Adnotacje/P_18A` | 1..1 | G41 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.P_18A` |
| `/Faktura/Fa/Adnotacje/Zwolnienie` | 1..1 | G41 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Annotations.Zwolnienie` |
| `/Faktura/Fa/Adnotacje/Zwolnienie/P_19` | 1..1 | G42 CHOICE[1..1] / G43 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.Zwolnienie.P_19` |
| `/Faktura/Fa/Adnotacje/Zwolnienie/P_19A` | 1..1 | G42 CHOICE[1..1] / G43 SEQUENCE[1..1] / G44 CHOICE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.Zwolnienie.P_19A` |
| `/Faktura/Fa/Adnotacje/Zwolnienie/P_19B` | 1..1 | G42 CHOICE[1..1] / G43 SEQUENCE[1..1] / G44 CHOICE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.Zwolnienie.P_19B` |
| `/Faktura/Fa/Adnotacje/Zwolnienie/P_19C` | 1..1 | G42 CHOICE[1..1] / G43 SEQUENCE[1..1] / G44 CHOICE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.Zwolnienie.P_19C` |
| `/Faktura/Fa/Adnotacje/Zwolnienie/P_19N` | 1..1 | G42 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.Zwolnienie.P_19N` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu` | 1..1 | G41 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Annotations.NoweSrodkiTransportu` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/P_22` | 1..1 | G45 CHOICE[1..1] / G46 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.NoweSrodkiTransportu.P_22` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/P_42_5` | 1..1 | G45 CHOICE[1..1] / G46 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.NoweSrodkiTransportu.P_42_5` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | 1..10000 | G45 CHOICE[1..1] / G46 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22A` | 1..1 | G47 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22A` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_NrWierszaNST` | 1..1 | G47 SEQUENCE[1..1] | tns:TNaturalny [base=etd:TNaturalny; MinExclusive=0] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_NrWierszaNST` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22BMK` | 0..1 | G47 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22BMK` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22BMD` | 0..1 | G47 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22BMD` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22BK` | 0..1 | G47 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22BK` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22BNR` | 0..1 | G47 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22BNR` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22BRP` | 0..1 | G47 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22BRP` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22B` | 1..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22B` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22B1` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] / G50 CHOICE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22B1` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22B2` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] / G50 CHOICE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22B2` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22B3` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] / G50 CHOICE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22B3` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22B4` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] / G50 CHOICE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22B4` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22BT` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22BT` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22C` | 1..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G51 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22C` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22C1` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G51 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22C1` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22D` | 1..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G52 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22D` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu/P_22D1` | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G52 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Annotations.NoweSrodkiTransportu.NowySrodekTransportu.P_22D1` |
| `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/P_22N` | 1..1 | G45 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.NoweSrodkiTransportu.P_22N` |
| `/Faktura/Fa/Adnotacje/P_23` | 1..1 | G41 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.P_23` |
| `/Faktura/Fa/Adnotacje/PMarzy` | 1..1 | G41 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Annotations.PMarzy` |
| `/Faktura/Fa/Adnotacje/PMarzy/P_PMarzy` | 1..1 | G53 CHOICE[1..1] / G54 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.PMarzy.P_PMarzy` |
| `/Faktura/Fa/Adnotacje/PMarzy/P_PMarzy_2` | 1..1 | G53 CHOICE[1..1] / G54 SEQUENCE[1..1] / G55 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.PMarzy.P_PMarzy_2` |
| `/Faktura/Fa/Adnotacje/PMarzy/P_PMarzy_3_1` | 1..1 | G53 CHOICE[1..1] / G54 SEQUENCE[1..1] / G55 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.PMarzy.P_PMarzy_3_1` |
| `/Faktura/Fa/Adnotacje/PMarzy/P_PMarzy_3_2` | 1..1 | G53 CHOICE[1..1] / G54 SEQUENCE[1..1] / G55 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.PMarzy.P_PMarzy_3_2` |
| `/Faktura/Fa/Adnotacje/PMarzy/P_PMarzy_3_3` | 1..1 | G53 CHOICE[1..1] / G54 SEQUENCE[1..1] / G55 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.PMarzy.P_PMarzy_3_3` |
| `/Faktura/Fa/Adnotacje/PMarzy/P_PMarzyN` | 1..1 | G53 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Annotations.PMarzy.P_PMarzyN` |
| `/Faktura/Fa/RodzajFaktury` | 1..1 | G33 SEQUENCE[1..1] | tns:TRodzajFaktury [base=tns:TZnakowy; Enumeration=VAT; Enumeration=KOR; Enumeration=ZAL; Enumeration=ROZ; Enumeration=UPR; Enumeration=KOR_ZAL; Enumeration=KOR_ROZ] | string (kody: enum/value object) | `SalesInvoice.Kind` |
| `/Faktura/Fa/PrzyczynaKorekty` | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.FaDetails.PrzyczynaKorekty` |
| `/Faktura/Fa/TypKorekty` | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | tns:TTypKorekty [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.FaDetails.TypKorekty` |
| `/Faktura/Fa/DaneFaKorygowanej` | 1..50000 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.References[]` |
| `/Faktura/Fa/DaneFaKorygowanej/DataWystFaKorygowanej` | 1..1 | G57 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.Correction.References[].DataWystFaKorygowanej` |
| `/Faktura/Fa/DaneFaKorygowanej/NrFaKorygowanej` | 1..1 | G57 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Correction.References[].NrFaKorygowanej` |
| `/Faktura/Fa/DaneFaKorygowanej/NrKSeF` | 1..1 | G57 SEQUENCE[1..1] / G58 CHOICE[1..1] / G59 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Correction.References[].NrKSeF` |
| `/Faktura/Fa/DaneFaKorygowanej/NrKSeFFaKorygowanej` | 1..1 | G57 SEQUENCE[1..1] / G58 CHOICE[1..1] / G59 SEQUENCE[1..1] | tns:TNumerKSeF [base=xs:token; Pattern=([1-9]((\d[1-9])&#124;([1-9]\d))\d{7}&#124;M\d{9}&#124;[A-Z]{3}\d{7})-(20[2-9][0-9]&#124;2[1-9][0-9]{2}&#124;[3-9][0-9]{3})(0[1-9]&#124;1[0-2])(0[1-9]&#124;[1-2][0-9]&#124;3[0-1])-([0-9A-F]{6})-?([0-9A-F]{6})-([0-9A-F]{2})] | string (kody: enum/value object) | `SalesInvoice.Correction.References[].NrKSeFFaKorygowanej` |
| `/Faktura/Fa/DaneFaKorygowanej/NrKSeFN` | 1..1 | G57 SEQUENCE[1..1] / G58 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Correction.References[].NrKSeFN` |
| `/Faktura/Fa/OkresFaKorygowanej` | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.FaDetails.OkresFaKorygowanej` |
| `/Faktura/Fa/NrFaKorygowany` | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.FaDetails.NrFaKorygowany` |
| `/Faktura/Fa/Podmiot1K` | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.OriginalSeller` |
| `/Faktura/Fa/Podmiot1K/PrefiksPodatnika` | 0..1 | G60 SEQUENCE[1..1] | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.PrefiksPodatnika` |
| `/Faktura/Fa/Podmiot1K/DaneIdentyfikacyjne` | 1..1 | G60 SEQUENCE[1..1] | tns:TPodmiot1 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.OriginalSeller.DaneIdentyfikacyjne` |
| `/Faktura/Fa/Podmiot1K/DaneIdentyfikacyjne/NIP` | 1..1 | G61 SEQUENCE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.DaneIdentyfikacyjne.NIP` |
| `/Faktura/Fa/Podmiot1K/DaneIdentyfikacyjne/Nazwa` | 1..1 | G61 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/Fa/Podmiot1K/Adres` | 1..1 | G60 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.OriginalSeller.Adres` |
| `/Faktura/Fa/Podmiot1K/Adres/KodKraju` | 1..1 | G62 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.Adres.KodKraju` |
| `/Faktura/Fa/Podmiot1K/Adres/AdresL1` | 1..1 | G62 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.Adres.AdresL1` |
| `/Faktura/Fa/Podmiot1K/Adres/AdresL2` | 0..1 | G62 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.Adres.AdresL2` |
| `/Faktura/Fa/Podmiot1K/Adres/GLN` | 0..1 | G62 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalSeller.Adres.GLN` |
| `/Faktura/Fa/Podmiot2K` | 0..101 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.OriginalBuyers[]` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne` | 1..1 | G63 SEQUENCE[1..1] | tns:TPodmiot2 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/NIP` | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.NIP` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/KodUE` | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] / G66 SEQUENCE[1..1] | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.KodUE` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/NrVatUE` | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] / G66 SEQUENCE[1..1] | tns:TNrVatUE [base=xs:string; Pattern=(\d&#124;[A-Z]&#124;\+&#124;\*){1,12}] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.NrVatUE` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/KodKraju` | 0..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] / G67 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.KodKraju` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/NrID` | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] / G67 SEQUENCE[1..1] | inline [base=etd:TNrIdentyfikacjiPodatkowej; ] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.NrID` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/BrakID` | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.BrakID` |
| `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne/Nazwa` | 1..1 | G64 SEQUENCE[1..1] / G68 SEQUENCE[0..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/Fa/Podmiot2K/Adres` | 0..1 | G63 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Correction.OriginalBuyers[].Adres` |
| `/Faktura/Fa/Podmiot2K/Adres/KodKraju` | 1..1 | G69 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].Adres.KodKraju` |
| `/Faktura/Fa/Podmiot2K/Adres/AdresL1` | 1..1 | G69 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].Adres.AdresL1` |
| `/Faktura/Fa/Podmiot2K/Adres/AdresL2` | 0..1 | G69 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].Adres.AdresL2` |
| `/Faktura/Fa/Podmiot2K/Adres/GLN` | 0..1 | G69 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].Adres.GLN` |
| `/Faktura/Fa/Podmiot2K/IDNabywcy` | 0..1 | G63 SEQUENCE[1..1] | inline [base=tns:TZnakowy50; MaxLength=32] | string (kody: enum/value object) | `SalesInvoice.Correction.OriginalBuyers[].IDNabywcy` |
| `/Faktura/Fa/P_15ZK` | 1..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] / G70 SEQUENCE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.FaDetails.P_15ZK` |
| `/Faktura/Fa/KursWalutyZK` | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] / G70 SEQUENCE[0..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.FaDetails.KursWalutyZK` |
| `/Faktura/Fa/ZaliczkaCzesciowa` | 0..31 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AdvanceReceipts[]` |
| `/Faktura/Fa/ZaliczkaCzesciowa/P_6Z` | 1..1 | G71 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.AdvanceReceipts[].P_6Z` |
| `/Faktura/Fa/ZaliczkaCzesciowa/P_15Z` | 1..1 | G71 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.AdvanceReceipts[].P_15Z` |
| `/Faktura/Fa/ZaliczkaCzesciowa/KursWalutyZW` | 0..1 | G71 SEQUENCE[1..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.AdvanceReceipts[].KursWalutyZW` |
| `/Faktura/Fa/FP` | 0..1 | G33 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.FaDetails.FP` |
| `/Faktura/Fa/TP` | 0..1 | G33 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.FaDetails.TP` |
| `/Faktura/Fa/DodatkowyOpis` | 0..10000 | G33 SEQUENCE[1..1] | tns:TKluczWartosc | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AdditionalDescriptions[]` |
| `/Faktura/Fa/DodatkowyOpis/NrWiersza` | 0..1 | G72 SEQUENCE[1..1] | tns:TNaturalny [base=etd:TNaturalny; MinExclusive=0] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.AdditionalDescriptions[].NrWiersza` |
| `/Faktura/Fa/DodatkowyOpis/Klucz` | 1..1 | G72 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.AdditionalDescriptions[].Klucz` |
| `/Faktura/Fa/DodatkowyOpis/Wartosc` | 1..1 | G72 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.AdditionalDescriptions[].Wartosc` |
| `/Faktura/Fa/FakturaZaliczkowa` | 0..100 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.AdvanceReferences[]` |
| `/Faktura/Fa/FakturaZaliczkowa/NrKSeFZN` | 1..1 | G73 CHOICE[1..1] / G74 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.AdvanceReferences[].NrKSeFZN` |
| `/Faktura/Fa/FakturaZaliczkowa/NrFaZaliczkowej` | 1..1 | G73 CHOICE[1..1] / G74 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.AdvanceReferences[].NrFaZaliczkowej` |
| `/Faktura/Fa/FakturaZaliczkowa/NrKSeFFaZaliczkowej` | 1..1 | G73 CHOICE[1..1] | tns:TNumerKSeF [base=xs:token; Pattern=([1-9]((\d[1-9])&#124;([1-9]\d))\d{7}&#124;M\d{9}&#124;[A-Z]{3}\d{7})-(20[2-9][0-9]&#124;2[1-9][0-9]{2}&#124;[3-9][0-9]{3})(0[1-9]&#124;1[0-2])(0[1-9]&#124;[1-2][0-9]&#124;3[0-1])-([0-9A-F]{6})-?([0-9A-F]{6})-([0-9A-F]{2})] | string (kody: enum/value object) | `SalesInvoice.AdvanceReferences[].NrKSeFFaZaliczkowej` |
| `/Faktura/Fa/ZwrotAkcyzy` | 0..1 | G33 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.FaDetails.ZwrotAkcyzy` |
| `/Faktura/Fa/FaWiersz` | 0..10000 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Lines[]` |
| `/Faktura/Fa/FaWiersz/NrWierszaFa` | 1..1 | G75 SEQUENCE[1..1] | tns:TNaturalny [base=etd:TNaturalny; MinExclusive=0] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Lines[].NrWierszaFa` |
| `/Faktura/Fa/FaWiersz/UU_ID` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Lines[].UU_ID` |
| `/Faktura/Fa/FaWiersz/P_6A` | 0..1 | G75 SEQUENCE[1..1] | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.Lines[].P_6A` |
| `/Faktura/Fa/FaWiersz/P_7` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Lines[].P_7` |
| `/Faktura/Fa/FaWiersz/Indeks` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Lines[].Indeks` |
| `/Faktura/Fa/FaWiersz/GTIN` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy20 [base=xs:token; MinLength=1; MaxLength=20] | string (kody: enum/value object) | `SalesInvoice.Lines[].GTIN` |
| `/Faktura/Fa/FaWiersz/PKWiU` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Lines[].PKWiU` |
| `/Faktura/Fa/FaWiersz/CN` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Lines[].CN` |
| `/Faktura/Fa/FaWiersz/PKOB` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Lines[].PKOB` |
| `/Faktura/Fa/FaWiersz/P_8A` | 0..1 | G75 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Lines[].P_8A` |
| `/Faktura/Fa/FaWiersz/P_8B` | 0..1 | G75 SEQUENCE[1..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.Lines[].P_8B` |
| `/Faktura/Fa/FaWiersz/P_9A` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy2 [base=xs:decimal; TotalDigits=22; FractionDigits=8; Pattern=-?([1-9]\d{0,13}&#124;0)(\.\d{1,8})?] | decimal | `SalesInvoice.Lines[].P_9A` |
| `/Faktura/Fa/FaWiersz/P_9B` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy2 [base=xs:decimal; TotalDigits=22; FractionDigits=8; Pattern=-?([1-9]\d{0,13}&#124;0)(\.\d{1,8})?] | decimal | `SalesInvoice.Lines[].P_9B` |
| `/Faktura/Fa/FaWiersz/P_10` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy2 [base=xs:decimal; TotalDigits=22; FractionDigits=8; Pattern=-?([1-9]\d{0,13}&#124;0)(\.\d{1,8})?] | decimal | `SalesInvoice.Lines[].P_10` |
| `/Faktura/Fa/FaWiersz/P_11` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Lines[].P_11` |
| `/Faktura/Fa/FaWiersz/P_11A` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Lines[].P_11A` |
| `/Faktura/Fa/FaWiersz/P_11Vat` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Lines[].P_11Vat` |
| `/Faktura/Fa/FaWiersz/P_12` | 0..1 | G75 SEQUENCE[1..1] | tns:TStawkaPodatku [base=tns:TZnakowy; Enumeration=23; Enumeration=22; Enumeration=8; Enumeration=7; Enumeration=5; Enumeration=4; Enumeration=3; Enumeration=0 KR; Enumeration=0 WDT; Enumeration=0 EX; Enumeration=zw; Enumeration=oo; Enumeration=np I; Enumeration=np II] | string (kody: enum/value object) | `SalesInvoice.Lines[].P_12` |
| `/Faktura/Fa/FaWiersz/P_12_XII` | 0..1 | G75 SEQUENCE[1..1] | tns:TProcentowy [base=xs:decimal; TotalDigits=9; FractionDigits=6; MinInclusive=0; MaxInclusive=100; WhiteSpace=collapse] | decimal | `SalesInvoice.Lines[].P_12_XII` |
| `/Faktura/Fa/FaWiersz/P_12_Zal_15` | 0..1 | G75 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Lines[].P_12_Zal_15` |
| `/Faktura/Fa/FaWiersz/KwotaAkcyzy` | 0..1 | G75 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Lines[].KwotaAkcyzy` |
| `/Faktura/Fa/FaWiersz/GTU` | 0..1 | G75 SEQUENCE[1..1] | tns:TGTU [base=tns:TZnakowy; Enumeration=GTU_01; Enumeration=GTU_02; Enumeration=GTU_03; Enumeration=GTU_04; Enumeration=GTU_05; Enumeration=GTU_06; Enumeration=GTU_07; Enumeration=GTU_08; Enumeration=GTU_09; Enumeration=GTU_10; Enumeration=GTU_11; Enumeration=GTU_12; Enumeration=GTU_13] | string (kody: enum/value object) | `SalesInvoice.Lines[].GTU` |
| `/Faktura/Fa/FaWiersz/Procedura` | 0..1 | G75 SEQUENCE[1..1] | tns:TOznaczenieProcedury [base=tns:TZnakowy; Enumeration=WSTO_EE; Enumeration=IED; Enumeration=TT_D; Enumeration=I_42; Enumeration=I_63; Enumeration=B_SPV; Enumeration=B_SPV_DOSTAWA; Enumeration=B_MPV_PROWIZJA] | string (kody: enum/value object) | `SalesInvoice.Lines[].Procedura` |
| `/Faktura/Fa/FaWiersz/KursWaluty` | 0..1 | G75 SEQUENCE[1..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.Lines[].KursWaluty` |
| `/Faktura/Fa/FaWiersz/StanPrzed` | 0..1 | G75 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Lines[].StanPrzed` |
| `/Faktura/Fa/Rozliczenie` | 0..1 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Settlement` |
| `/Faktura/Fa/Rozliczenie/Obciazenia` | 0..100 | G76 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Settlement.Obciazenia` |
| `/Faktura/Fa/Rozliczenie/Obciazenia/Kwota` | 1..1 | G77 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Settlement.Obciazenia.Kwota` |
| `/Faktura/Fa/Rozliczenie/Obciazenia/Powod` | 1..1 | G77 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Settlement.Obciazenia.Powod` |
| `/Faktura/Fa/Rozliczenie/SumaObciazen` | 0..1 | G76 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Settlement.SumaObciazen` |
| `/Faktura/Fa/Rozliczenie/Odliczenia` | 0..100 | G76 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Settlement.Odliczenia` |
| `/Faktura/Fa/Rozliczenie/Odliczenia/Kwota` | 1..1 | G78 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Settlement.Odliczenia.Kwota` |
| `/Faktura/Fa/Rozliczenie/Odliczenia/Powod` | 1..1 | G78 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Settlement.Odliczenia.Powod` |
| `/Faktura/Fa/Rozliczenie/SumaOdliczen` | 0..1 | G76 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Settlement.SumaOdliczen` |
| `/Faktura/Fa/Rozliczenie/DoZaplaty` | 1..1 | G76 SEQUENCE[1..1] / G79 CHOICE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Settlement.DoZaplaty` |
| `/Faktura/Fa/Rozliczenie/DoRozliczenia` | 1..1 | G76 SEQUENCE[1..1] / G79 CHOICE[0..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Settlement.DoRozliczenia` |
| `/Faktura/Fa/Platnosc` | 0..1 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment` |
| `/Faktura/Fa/Platnosc/Zaplacono` | 1..1 | G80 SEQUENCE[1..1] / G81 CHOICE[0..1] / G82 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.Zaplacono` |
| `/Faktura/Fa/Platnosc/DataZaplaty` | 1..1 | G80 SEQUENCE[1..1] / G81 CHOICE[0..1] / G82 SEQUENCE[1..1] | tns:TData [base=etd:TData; MinInclusive=2016-07-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.Payment.DataZaplaty` |
| `/Faktura/Fa/Platnosc/ZnacznikZaplatyCzesciowej` | 1..1 | G80 SEQUENCE[1..1] / G81 CHOICE[0..1] / G83 SEQUENCE[1..1] | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.ZnacznikZaplatyCzesciowej` |
| `/Faktura/Fa/Platnosc/ZaplataCzesciowa` | 1..100 | G80 SEQUENCE[1..1] / G81 CHOICE[0..1] / G83 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment.ZaplataCzesciowa` |
| `/Faktura/Fa/Platnosc/ZaplataCzesciowa/KwotaZaplatyCzesciowej` | 1..1 | G84 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Payment.ZaplataCzesciowa.KwotaZaplatyCzesciowej` |
| `/Faktura/Fa/Platnosc/ZaplataCzesciowa/DataZaplatyCzesciowej` | 1..1 | G84 SEQUENCE[1..1] | tns:TData [base=etd:TData; MinInclusive=2016-07-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.Payment.ZaplataCzesciowa.DataZaplatyCzesciowej` |
| `/Faktura/Fa/Platnosc/ZaplataCzesciowa/FormaPlatnosci` | 1..1 | G84 SEQUENCE[1..1] / G85 CHOICE[0..1] | tns:TFormaPlatnosci [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.ZaplataCzesciowa.FormaPlatnosci` |
| `/Faktura/Fa/Platnosc/ZaplataCzesciowa/PlatnoscInna` | 1..1 | G84 SEQUENCE[1..1] / G85 CHOICE[0..1] / G86 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.ZaplataCzesciowa.PlatnoscInna` |
| `/Faktura/Fa/Platnosc/ZaplataCzesciowa/OpisPlatnosci` | 1..1 | G84 SEQUENCE[1..1] / G85 CHOICE[0..1] / G86 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.ZaplataCzesciowa.OpisPlatnosci` |
| `/Faktura/Fa/Platnosc/TerminPlatnosci` | 0..100 | G80 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment.TerminPlatnosci` |
| `/Faktura/Fa/Platnosc/TerminPlatnosci/Termin` | 0..1 | G87 SEQUENCE[0..1] | tns:TData [base=etd:TData; MinInclusive=2016-07-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.Payment.TerminPlatnosci.Termin` |
| `/Faktura/Fa/Platnosc/TerminPlatnosci/TerminOpis` | 0..1 | G87 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment.TerminPlatnosci.TerminOpis` |
| `/Faktura/Fa/Platnosc/TerminPlatnosci/TerminOpis/Ilosc` | 1..1 | G88 SEQUENCE[1..1] | xs:integer [base=xs:decimal; ] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.TerminPlatnosci.TerminOpis.Ilosc` |
| `/Faktura/Fa/Platnosc/TerminPlatnosci/TerminOpis/Jednostka` | 1..1 | G88 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Payment.TerminPlatnosci.TerminOpis.Jednostka` |
| `/Faktura/Fa/Platnosc/TerminPlatnosci/TerminOpis/ZdarzeniePoczatkowe` | 1..1 | G88 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.TerminPlatnosci.TerminOpis.ZdarzeniePoczatkowe` |
| `/Faktura/Fa/Platnosc/FormaPlatnosci` | 1..1 | G80 SEQUENCE[1..1] / G89 CHOICE[0..1] | tns:TFormaPlatnosci [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.FormaPlatnosci` |
| `/Faktura/Fa/Platnosc/PlatnoscInna` | 1..1 | G80 SEQUENCE[1..1] / G89 CHOICE[0..1] / G90 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.PlatnoscInna` |
| `/Faktura/Fa/Platnosc/OpisPlatnosci` | 1..1 | G80 SEQUENCE[1..1] / G89 CHOICE[0..1] / G90 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.OpisPlatnosci` |
| `/Faktura/Fa/Platnosc/RachunekBankowy` | 0..100 | G80 SEQUENCE[1..1] | tns:TRachunekBankowy | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment.RachunekBankowy` |
| `/Faktura/Fa/Platnosc/RachunekBankowy/NrRB` | 1..1 | G91 SEQUENCE[1..1] | tns:TNrRB [base=xs:string; MinLength=10; MaxLength=34] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowy.NrRB` |
| `/Faktura/Fa/Platnosc/RachunekBankowy/SWIFT` | 0..1 | G91 SEQUENCE[1..1] | tns:SWIFT_Type [base=xs:string; Pattern=[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3}){0,1}] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowy.SWIFT` |
| `/Faktura/Fa/Platnosc/RachunekBankowy/RachunekWlasnyBanku` | 0..1 | G91 SEQUENCE[1..1] | tns:TRachunekWlasnyBanku [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.RachunekBankowy.RachunekWlasnyBanku` |
| `/Faktura/Fa/Platnosc/RachunekBankowy/NazwaBanku` | 0..1 | G91 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowy.NazwaBanku` |
| `/Faktura/Fa/Platnosc/RachunekBankowy/OpisRachunku` | 0..1 | G91 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowy.OpisRachunku` |
| `/Faktura/Fa/Platnosc/RachunekBankowyFaktora` | 0..20 | G80 SEQUENCE[1..1] | tns:TRachunekBankowy | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment.RachunekBankowyFaktora` |
| `/Faktura/Fa/Platnosc/RachunekBankowyFaktora/NrRB` | 1..1 | G92 SEQUENCE[1..1] | tns:TNrRB [base=xs:string; MinLength=10; MaxLength=34] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowyFaktora.NrRB` |
| `/Faktura/Fa/Platnosc/RachunekBankowyFaktora/SWIFT` | 0..1 | G92 SEQUENCE[1..1] | tns:SWIFT_Type [base=xs:string; Pattern=[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3}){0,1}] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowyFaktora.SWIFT` |
| `/Faktura/Fa/Platnosc/RachunekBankowyFaktora/RachunekWlasnyBanku` | 0..1 | G92 SEQUENCE[1..1] | tns:TRachunekWlasnyBanku [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Payment.RachunekBankowyFaktora.RachunekWlasnyBanku` |
| `/Faktura/Fa/Platnosc/RachunekBankowyFaktora/NazwaBanku` | 0..1 | G92 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowyFaktora.NazwaBanku` |
| `/Faktura/Fa/Platnosc/RachunekBankowyFaktora/OpisRachunku` | 0..1 | G92 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.RachunekBankowyFaktora.OpisRachunku` |
| `/Faktura/Fa/Platnosc/Skonto` | 0..1 | G80 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Payment.Skonto` |
| `/Faktura/Fa/Platnosc/Skonto/WarunkiSkonta` | 1..1 | G93 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.Skonto.WarunkiSkonta` |
| `/Faktura/Fa/Platnosc/Skonto/WysokoscSkonta` | 1..1 | G93 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Payment.Skonto.WysokoscSkonta` |
| `/Faktura/Fa/Platnosc/LinkDoPlatnosci` | 0..1 | G80 SEQUENCE[1..1] | inline [base=tns:TZnakowy512; Pattern=(https?)://([a-zA-Z0-9][a-zA-Z0-9-]*\.)+[a-zA-Z]{2,}(:[0-9]{1,5})?(/[^\s?#]*)?\?([^#\s]*&)?IPKSeF=[0-9]{3}[a-zA-Z0-9]{10}(&[^#\s]*)?(#.*)?] | string (kody: enum/value object) | `SalesInvoice.Payment.LinkDoPlatnosci` |
| `/Faktura/Fa/Platnosc/IPKSeF` | 0..1 | G80 SEQUENCE[1..1] | inline [base=tns:TZnakowy20; MaxLength=13; Pattern=[0-9]{3}[a-zA-Z0-9]{10}] | string (kody: enum/value object) | `SalesInvoice.Payment.IPKSeF` |
| `/Faktura/Fa/WarunkiTransakcji` | 0..1 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms` |
| `/Faktura/Fa/WarunkiTransakcji/Umowy` | 0..100 | G94 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Umowy` |
| `/Faktura/Fa/WarunkiTransakcji/Umowy/DataUmowy` | 0..1 | G95 SEQUENCE[0..1] | tns:TDataU [base=etd:TData; MinInclusive=1990-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.TransactionTerms.Umowy.DataUmowy` |
| `/Faktura/Fa/WarunkiTransakcji/Umowy/NrUmowy` | 0..1 | G95 SEQUENCE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Umowy.NrUmowy` |
| `/Faktura/Fa/WarunkiTransakcji/Zamowienia` | 0..100 | G94 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Zamowienia` |
| `/Faktura/Fa/WarunkiTransakcji/Zamowienia/DataZamowienia` | 0..1 | G96 SEQUENCE[0..1] | tns:TDataU [base=etd:TData; MinInclusive=1990-01-01; MaxInclusive=2050-01-01] | DateOnly | `SalesInvoice.TransactionTerms.Zamowienia.DataZamowienia` |
| `/Faktura/Fa/WarunkiTransakcji/Zamowienia/NrZamowienia` | 0..1 | G96 SEQUENCE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Zamowienia.NrZamowienia` |
| `/Faktura/Fa/WarunkiTransakcji/NrPartiiTowaru` | 0..1000 | G94 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.NrPartiiTowaru` |
| `/Faktura/Fa/WarunkiTransakcji/WarunkiDostawy` | 0..1 | G94 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.WarunkiDostawy` |
| `/Faktura/Fa/WarunkiTransakcji/KursUmowny` | 1..1 | G94 SEQUENCE[1..1] / G97 SEQUENCE[0..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.TransactionTerms.KursUmowny` |
| `/Faktura/Fa/WarunkiTransakcji/WalutaUmowna` | 1..1 | G94 SEQUENCE[1..1] / G97 SEQUENCE[0..1] | tns:TKodWaluty [base=xs:normalizedString; Enumeration=AED; Enumeration=AFN; Enumeration=ALL; Enumeration=AMD; Enumeration=ANG; Enumeration=AOA; Enumeration=ARS; Enumeration=AUD; Enumeration=AWG; Enumeration=AZN; Enumeration=BAM; Enumeration=BBD; Enumeration=BDT; Enumeration=BGN; Enumeration=BHD; Enumeration=BIF; Enumeration=BMD; Enumeration=BND; Enumeration=BOB; Enumeration=BOV; Enumeration=BRL; Enumeration=BSD; Enumeration=BTN; Enumeration=BWP; Enumeration=BYN; Enumeration=BZD; Enumeration=CAD; Enumeration=CDF; Enumeration=CHE; Enumeration=CHF; Enumeration=CHW; Enumeration=CLF; Enumeration=CLP; Enumeration=CNY; Enumeration=COP; Enumeration=COU; Enumeration=CRC; Enumeration=CUC; Enumeration=CUP; Enumeration=CVE; Enumeration=CZK; Enumeration=DJF; Enumeration=DKK; Enumeration=DOP; Enumeration=DZD; Enumeration=EGP; Enumeration=ERN; Enumeration=ETB; Enumeration=EUR; Enumeration=FJD; Enumeration=FKP; Enumeration=GBP; Enumeration=GEL; Enumeration=GGP; Enumeration=GHS; Enumeration=GIP; Enumeration=GMD; Enumeration=GNF; Enumeration=GTQ; Enumeration=GYD; Enumeration=HKD; Enumeration=HNL; Enumeration=HRK; Enumeration=HTG; Enumeration=HUF; Enumeration=IDR; Enumeration=ILS; Enumeration=IMP; Enumeration=INR; Enumeration=IQD; Enumeration=IRR; Enumeration=ISK; Enumeration=JEP; Enumeration=JMD; Enumeration=JOD; Enumeration=JPY; Enumeration=KES; Enumeration=KGS; Enumeration=KHR; Enumeration=KMF; Enumeration=KPW; Enumeration=KRW; Enumeration=KWD; Enumeration=KYD; Enumeration=KZT; Enumeration=LAK; Enumeration=LBP; Enumeration=LKR; Enumeration=LRD; Enumeration=LSL; Enumeration=LYD; Enumeration=MAD; Enumeration=MDL; Enumeration=MGA; Enumeration=MKD; Enumeration=MMK; Enumeration=MNT; Enumeration=MOP; Enumeration=MRU; Enumeration=MUR; Enumeration=MVR; Enumeration=MWK; Enumeration=MXN; Enumeration=MXV; Enumeration=MYR; Enumeration=MZN; Enumeration=NAD; Enumeration=NGN; Enumeration=NIO; Enumeration=NOK; Enumeration=NPR; Enumeration=NZD; Enumeration=OMR; Enumeration=PAB; Enumeration=PEN; Enumeration=PGK; Enumeration=PHP; Enumeration=PKR; Enumeration=PLN; Enumeration=PYG; Enumeration=QAR; Enumeration=RON; Enumeration=RSD; Enumeration=RUB; Enumeration=RWF; Enumeration=SAR; Enumeration=SBD; Enumeration=SCR; Enumeration=SDG; Enumeration=SEK; Enumeration=SGD; Enumeration=SHP; Enumeration=SLL; Enumeration=SOS; Enumeration=SRD; Enumeration=SSP; Enumeration=STN; Enumeration=SVC; Enumeration=SYP; Enumeration=SZL; Enumeration=THB; Enumeration=TJS; Enumeration=TMT; Enumeration=TND; Enumeration=TOP; Enumeration=TRY; Enumeration=TTD; Enumeration=TWD; Enumeration=TZS; Enumeration=UAH; Enumeration=UGX; Enumeration=USD; Enumeration=USN; Enumeration=UYI; Enumeration=UYU; Enumeration=UYW; Enumeration=UZS; Enumeration=VES; Enumeration=VND; Enumeration=VUV; Enumeration=WST; Enumeration=XAF; Enumeration=XAG; Enumeration=XAU; Enumeration=XBA; Enumeration=XBB; Enumeration=XBC; Enumeration=XBD; Enumeration=XCD; Enumeration=XCG; Enumeration=XDR; Enumeration=XOF; Enumeration=XPD; Enumeration=XPF; Enumeration=XPT; Enumeration=XSU; Enumeration=XUA; Enumeration=XXX; Enumeration=YER; Enumeration=ZAR; Enumeration=ZMW; Enumeration=ZWL] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.WalutaUmowna` |
| `/Faktura/Fa/WarunkiTransakcji/Transport` | 0..20 | G94 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/RodzajTransportu` | 1..1 | G98 SEQUENCE[1..1] / G99 CHOICE[1..1] | tns:TRodzajTransportu [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=7; Enumeration=8] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.TransactionTerms.Transport.RodzajTransportu` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/TransportInny` | 1..1 | G98 SEQUENCE[1..1] / G99 CHOICE[1..1] / G100 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.TransactionTerms.Transport.TransportInny` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/OpisInnegoTransportu` | 1..1 | G98 SEQUENCE[1..1] / G99 CHOICE[1..1] / G100 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.OpisInnegoTransportu` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik` | 0..1 | G98 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport.Przewoznik` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne` | 1..1 | G101 SEQUENCE[1..1] | tns:TPodmiot2 | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/NIP` | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.NIP` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/KodUE` | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] / G104 SEQUENCE[1..1] | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.KodUE` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/NrVatUE` | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] / G104 SEQUENCE[1..1] | tns:TNrVatUE [base=xs:string; Pattern=(\d&#124;[A-Z]&#124;\+&#124;\*){1,12}] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.NrVatUE` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/KodKraju` | 0..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] / G105 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.KodKraju` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/NrID` | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] / G105 SEQUENCE[1..1] | inline [base=etd:TNrIdentyfikacjiPodatkowej; ] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.NrID` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/BrakID` | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.BrakID` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne/Nazwa` | 1..1 | G102 SEQUENCE[1..1] / G106 SEQUENCE[0..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.DaneIdentyfikacyjne.Nazwa` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/AdresPrzewoznika` | 1..1 | G101 SEQUENCE[1..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport.Przewoznik.AdresPrzewoznika` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/AdresPrzewoznika/KodKraju` | 1..1 | G107 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.AdresPrzewoznika.KodKraju` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/AdresPrzewoznika/AdresL1` | 1..1 | G107 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.AdresPrzewoznika.AdresL1` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/AdresPrzewoznika/AdresL2` | 0..1 | G107 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.AdresPrzewoznika.AdresL2` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/AdresPrzewoznika/GLN` | 0..1 | G107 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.Przewoznik.AdresPrzewoznika.GLN` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/NrZleceniaTransportu` | 0..1 | G98 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.NrZleceniaTransportu` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/OpisLadunku` | 1..1 | G98 SEQUENCE[1..1] / G108 CHOICE[1..1] | tns:TLadunek [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7; Enumeration=8; Enumeration=9; Enumeration=10; Enumeration=11; Enumeration=12; Enumeration=13; Enumeration=14; Enumeration=15; Enumeration=16; Enumeration=17; Enumeration=18; Enumeration=19; Enumeration=20] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.TransactionTerms.Transport.OpisLadunku` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/LadunekInny` | 1..1 | G98 SEQUENCE[1..1] / G108 CHOICE[1..1] / G109 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.TransactionTerms.Transport.LadunekInny` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/OpisInnegoLadunku` | 1..1 | G98 SEQUENCE[1..1] / G108 CHOICE[1..1] / G109 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.OpisInnegoLadunku` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/JednostkaOpakowania` | 0..1 | G98 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.JednostkaOpakowania` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/DataGodzRozpTransportu` | 0..1 | G98 SEQUENCE[1..1] / G110 SEQUENCE[0..1] | tns:TDataCzas [base=etd:TDataCzas; MinInclusive=2021-10-01T00:00:00Z; MaxInclusive=2050-01-01T23:59:59Z] | DateTimeOffset | `SalesInvoice.TransactionTerms.Transport.DataGodzRozpTransportu` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/DataGodzZakTransportu` | 0..1 | G98 SEQUENCE[1..1] / G110 SEQUENCE[0..1] | tns:TDataCzas [base=etd:TDataCzas; MinInclusive=2021-10-01T00:00:00Z; MaxInclusive=2050-01-01T23:59:59Z] | DateTimeOffset | `SalesInvoice.TransactionTerms.Transport.DataGodzZakTransportu` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaZ` | 0..1 | G98 SEQUENCE[1..1] / G110 SEQUENCE[0..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport.WysylkaZ` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaZ/KodKraju` | 1..1 | G111 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaZ.KodKraju` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaZ/AdresL1` | 1..1 | G111 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaZ.AdresL1` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaZ/AdresL2` | 0..1 | G111 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaZ.AdresL2` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaZ/GLN` | 0..1 | G111 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaZ.GLN` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaPrzez` | 0..20 | G98 SEQUENCE[1..1] / G110 SEQUENCE[0..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport.WysylkaPrzez` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaPrzez/KodKraju` | 1..1 | G112 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaPrzez.KodKraju` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaPrzez/AdresL1` | 1..1 | G112 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaPrzez.AdresL1` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaPrzez/AdresL2` | 0..1 | G112 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaPrzez.AdresL2` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaPrzez/GLN` | 0..1 | G112 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaPrzez.GLN` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaDo` | 0..1 | G98 SEQUENCE[1..1] / G110 SEQUENCE[0..1] | tns:TAdres | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.TransactionTerms.Transport.WysylkaDo` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaDo/KodKraju` | 1..1 | G113 SEQUENCE[1..1] | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaDo.KodKraju` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaDo/AdresL1` | 1..1 | G113 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaDo.AdresL1` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaDo/AdresL2` | 0..1 | G113 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaDo.AdresL2` |
| `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaDo/GLN` | 0..1 | G113 SEQUENCE[1..1] | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] | string (kody: enum/value object) | `SalesInvoice.TransactionTerms.Transport.WysylkaDo.GLN` |
| `/Faktura/Fa/WarunkiTransakcji/PodmiotPosredniczacy` | 0..1 | G94 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.TransactionTerms.PodmiotPosredniczacy` |
| `/Faktura/Fa/Zamowienie` | 0..1 | G33 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Order` |
| `/Faktura/Fa/Zamowienie/WartoscZamowienia` | 1..1 | G114 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Order.WartoscZamowienia` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz` | 1..10000 | G114 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Order.ZamowienieWiersz` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/NrWierszaZam` | 1..1 | G115 SEQUENCE[1..1] | tns:TNaturalny [base=etd:TNaturalny; MinExclusive=0] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Order.ZamowienieWiersz.NrWierszaZam` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/UU_IDZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.UU_IDZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_7Z` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.P_7Z` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/IndeksZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.IndeksZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/GTINZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy20 [base=xs:token; MinLength=1; MaxLength=20] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.GTINZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/PKWiUZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.PKWiUZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/CNZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.CNZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/PKOBZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.PKOBZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_8AZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.P_8AZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_8BZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] | decimal | `SalesInvoice.Order.ZamowienieWiersz.P_8BZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_9AZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TKwotowy2 [base=xs:decimal; TotalDigits=22; FractionDigits=8; Pattern=-?([1-9]\d{0,13}&#124;0)(\.\d{1,8})?] | decimal | `SalesInvoice.Order.ZamowienieWiersz.P_9AZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_11NettoZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Order.ZamowienieWiersz.P_11NettoZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_11VatZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Order.ZamowienieWiersz.P_11VatZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_12Z` | 0..1 | G115 SEQUENCE[1..1] | tns:TStawkaPodatku [base=tns:TZnakowy; Enumeration=23; Enumeration=22; Enumeration=8; Enumeration=7; Enumeration=5; Enumeration=4; Enumeration=3; Enumeration=0 KR; Enumeration=0 WDT; Enumeration=0 EX; Enumeration=zw; Enumeration=oo; Enumeration=np I; Enumeration=np II] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.P_12Z` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_12Z_XII` | 0..1 | G115 SEQUENCE[1..1] | tns:TProcentowy [base=xs:decimal; TotalDigits=9; FractionDigits=6; MinInclusive=0; MaxInclusive=100; WhiteSpace=collapse] | decimal | `SalesInvoice.Order.ZamowienieWiersz.P_12Z_XII` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/P_12Z_Zal_15` | 0..1 | G115 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Order.ZamowienieWiersz.P_12Z_Zal_15` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/GTUZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TGTU [base=tns:TZnakowy; Enumeration=GTU_01; Enumeration=GTU_02; Enumeration=GTU_03; Enumeration=GTU_04; Enumeration=GTU_05; Enumeration=GTU_06; Enumeration=GTU_07; Enumeration=GTU_08; Enumeration=GTU_09; Enumeration=GTU_10; Enumeration=GTU_11; Enumeration=GTU_12; Enumeration=GTU_13] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.GTUZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/ProceduraZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TOznaczenieProceduryZ [base=tns:TZnakowy; Enumeration=WSTO_EE; Enumeration=IED; Enumeration=TT_D; Enumeration=B_SPV; Enumeration=B_SPV_DOSTAWA; Enumeration=B_MPV_PROWIZJA] | string (kody: enum/value object) | `SalesInvoice.Order.ZamowienieWiersz.ProceduraZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/KwotaAkcyzyZ` | 0..1 | G115 SEQUENCE[1..1] | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] | decimal | `SalesInvoice.Order.ZamowienieWiersz.KwotaAkcyzyZ` |
| `/Faktura/Fa/Zamowienie/ZamowienieWiersz/StanPrzedZ` | 0..1 | G115 SEQUENCE[1..1] | etd:TWybor1 [base=xs:byte; Enumeration=1] | int (zakres sprawdzić z facetami; nieograniczone: string/BigInteger) | `SalesInvoice.Order.ZamowienieWiersz.StanPrzedZ` |
| `/Faktura/Stopka` | 0..1 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Footer` |
| `/Faktura/Stopka/Informacje` | 0..3 | G116 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Footer.Informacje` |
| `/Faktura/Stopka/Informacje/StopkaFaktury` | 0..1 | G117 SEQUENCE[0..1] | etd:TTekstowy [base=xs:string; MinLength=1; MaxLength=3500] | string (kody: enum/value object) | `SalesInvoice.Footer.Informacje.StopkaFaktury` |
| `/Faktura/Stopka/Rejestry` | 0..100 | G116 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Footer.Rejestry` |
| `/Faktura/Stopka/Rejestry/PelnaNazwa` | 0..1 | G118 SEQUENCE[0..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Footer.Rejestry.PelnaNazwa` |
| `/Faktura/Stopka/Rejestry/KRS` | 0..1 | G118 SEQUENCE[0..1] | etd:TNrKRS [base=xs:string; Pattern=\d{10}] | string (kody: enum/value object) | `SalesInvoice.Footer.Rejestry.KRS` |
| `/Faktura/Stopka/Rejestry/REGON` | 0..1 | G118 SEQUENCE[0..1] | etd:TNrREGON UNION(inline [base=xs:string; Pattern=\d{9}] OR inline [base=xs:string; Pattern=\d{14}]) | string (kody: enum/value object) | `SalesInvoice.Footer.Rejestry.REGON` |
| `/Faktura/Stopka/Rejestry/BDO` | 0..1 | G118 SEQUENCE[0..1] | inline [base=tns:TZnakowy; MaxLength=9] | string (kody: enum/value object) | `SalesInvoice.Footer.Rejestry.BDO` |
| `/Faktura/Zalacznik` | 0..1 | G1 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment` |
| `/Faktura/Zalacznik/BlokDanych` | 1..1000 | G119 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych` |
| `/Faktura/Zalacznik/BlokDanych/ZNaglowek` | 0..1 | G120 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.ZNaglowek` |
| `/Faktura/Zalacznik/BlokDanych/MetaDane` | 1..1000 | G120 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.MetaDane` |
| `/Faktura/Zalacznik/BlokDanych/MetaDane/ZKlucz` | 1..1 | G121 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.MetaDane.ZKlucz` |
| `/Faktura/Zalacznik/BlokDanych/MetaDane/ZWartosc` | 1..1 | G121 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.MetaDane.ZWartosc` |
| `/Faktura/Zalacznik/BlokDanych/Tekst` | 0..1 | G120 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tekst` |
| `/Faktura/Zalacznik/BlokDanych/Tekst/Akapit` | 1..10 | G122 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.Tekst.Akapit` |
| `/Faktura/Zalacznik/BlokDanych/Tabela` | 0..1000 | G120 SEQUENCE[1..1] / G123 SEQUENCE[0..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TMetaDane` | 0..1000 | G124 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela.TMetaDane` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TMetaDane/TKlucz` | 1..1 | G125 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.Tabela.TMetaDane.TKlucz` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TMetaDane/TWartosc` | 1..1 | G125 SEQUENCE[1..1] | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.Tabela.TMetaDane.TWartosc` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/Opis` | 0..1 | G124 SEQUENCE[1..1] | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.Tabela.Opis` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek` | 1..1 | G124 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela.TNaglowek` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek/Kol` | 1..20 | G126 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela.TNaglowek.Kol` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek/Kol/@Typ` | 1..1 | atrybut | inline [base=xs:string; Enumeration=date; Enumeration=datetime; Enumeration=dec; Enumeration=int; Enumeration=time; Enumeration=txt]; fixed= | string | `SalesInvoice.Attachment.BlokDanych.Tabela.TNaglowek.Kol.@Typ` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek/Kol/NKom` | 1..1 | G127 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela.TNaglowek.Kol.NKom` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek/Kol/NKom/#text` | 1..1 | simpleContent | tns:TZnakowy2 [base=xs:token; MinLength=0; MaxLength=256] | string | `SalesInvoice.Attachment.BlokDanych.Tabela.TNaglowek.Kol.NKom.Value` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/Wiersz` | 1..1000 | G124 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela.Wiersz` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/Wiersz/WKom` | 1..20 | G128 SEQUENCE[1..1] | tns:TZnakowy2 [base=xs:token; MinLength=0; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.Tabela.Wiersz.WKom` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/Suma` | 0..1 | G124 SEQUENCE[1..1] | complex inline | obiekt / kolekcja; patrz potomkowie | `SalesInvoice.Attachment.BlokDanych.Tabela.Suma` |
| `/Faktura/Zalacznik/BlokDanych/Tabela/Suma/SKom` | 1..20 | G129 SEQUENCE[1..1] | tns:TZnakowy2 [base=xs:token; MinLength=0; MaxLength=256] | string (kody: enum/value object) | `SalesInvoice.Attachment.BlokDanych.Tabela.Suma.SKom` |

## Grupy i wykluczenia

| ID | Rodzic | Rodzaj | Lokalnie | Grupy nadrzędne (w obrębie rodzica) | Dzieci w kolejności XSD |
|---|---|---|---|---|---|
| G1 | `/Faktura` | SEQUENCE | 1..1 |  | Naglowek → Podmiot1 → Podmiot2 → Podmiot3 → PodmiotUpowazniony → Fa → Stopka → Zalacznik |
| G2 | `/Faktura/Naglowek` | SEQUENCE | 1..1 |  | KodFormularza → WariantFormularza → DataWytworzeniaFa → SystemInfo |
| G3 | `/Faktura/Podmiot1` | SEQUENCE | 1..1 |  | PrefiksPodatnika → NrEORI → DaneIdentyfikacyjne → Adres → AdresKoresp → DaneKontaktowe → StatusInfoPodatnika |
| G4 | `/Faktura/Podmiot1/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | NIP → Nazwa |
| G5 | `/Faktura/Podmiot1/Adres` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G6 | `/Faktura/Podmiot1/AdresKoresp` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G7 | `/Faktura/Podmiot1/DaneKontaktowe` | SEQUENCE | 0..1 |  | Email → Telefon |
| G8 | `/Faktura/Podmiot2` | SEQUENCE | 1..1 |  | NrEORI → DaneIdentyfikacyjne → Adres → AdresKoresp → DaneKontaktowe → NrKlienta → IDNabywcy → JST → GV |
| G9 | `/Faktura/Podmiot2/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | Choice → Sequence |
| G10 | `/Faktura/Podmiot2/DaneIdentyfikacyjne` | CHOICE | 1..1 | G9 SEQUENCE[1..1] | NIP → Sequence → Sequence → BrakID |
| G11 | `/Faktura/Podmiot2/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] | KodUE → NrVatUE |
| G12 | `/Faktura/Podmiot2/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G9 SEQUENCE[1..1] / G10 CHOICE[1..1] | KodKraju → NrID |
| G13 | `/Faktura/Podmiot2/DaneIdentyfikacyjne` | SEQUENCE | 0..1 | G9 SEQUENCE[1..1] | Nazwa |
| G14 | `/Faktura/Podmiot2/Adres` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G15 | `/Faktura/Podmiot2/AdresKoresp` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G16 | `/Faktura/Podmiot2/DaneKontaktowe` | SEQUENCE | 0..1 |  | Email → Telefon |
| G17 | `/Faktura/Podmiot3` | SEQUENCE | 1..1 |  | IDNabywcy → NrEORI → DaneIdentyfikacyjne → Adres → AdresKoresp → DaneKontaktowe → Choice → Udzial → NrKlienta |
| G18 | `/Faktura/Podmiot3/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | Choice → Sequence |
| G19 | `/Faktura/Podmiot3/DaneIdentyfikacyjne` | CHOICE | 1..1 | G18 SEQUENCE[1..1] | NIP → IDWew → Sequence → Sequence → BrakID |
| G20 | `/Faktura/Podmiot3/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] | KodUE → NrVatUE |
| G21 | `/Faktura/Podmiot3/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G18 SEQUENCE[1..1] / G19 CHOICE[1..1] | KodKraju → NrID |
| G22 | `/Faktura/Podmiot3/DaneIdentyfikacyjne` | SEQUENCE | 0..1 | G18 SEQUENCE[1..1] | Nazwa |
| G23 | `/Faktura/Podmiot3/Adres` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G24 | `/Faktura/Podmiot3/AdresKoresp` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G25 | `/Faktura/Podmiot3/DaneKontaktowe` | SEQUENCE | 1..1 |  | Email → Telefon |
| G26 | `/Faktura/Podmiot3` | CHOICE | 1..1 | G17 SEQUENCE[1..1] | Rola → Sequence |
| G27 | `/Faktura/Podmiot3` | SEQUENCE | 1..1 | G17 SEQUENCE[1..1] / G26 CHOICE[1..1] | RolaInna → OpisRoli |
| G28 | `/Faktura/PodmiotUpowazniony` | SEQUENCE | 1..1 |  | NrEORI → DaneIdentyfikacyjne → Adres → AdresKoresp → DaneKontaktowe → RolaPU |
| G29 | `/Faktura/PodmiotUpowazniony/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | NIP → Nazwa |
| G30 | `/Faktura/PodmiotUpowazniony/Adres` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G31 | `/Faktura/PodmiotUpowazniony/AdresKoresp` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G32 | `/Faktura/PodmiotUpowazniony/DaneKontaktowe` | SEQUENCE | 1..1 |  | EmailPU → TelefonPU |
| G33 | `/Faktura/Fa` | SEQUENCE | 1..1 |  | KodWaluty → P_1 → P_1M → P_2 → WZ → Choice → Sequence → Sequence → Sequence → Sequence → Sequence → P_13_6_1 → P_13_6_2 → P_13_6_3 → P_13_7 → P_13_8 → P_13_9 → P_13_10 → P_13_11 → P_15 → KursWalutyZ → Adnotacje → RodzajFaktury → Sequence → ZaliczkaCzesciowa → FP → TP → DodatkowyOpis → FakturaZaliczkowa → ZwrotAkcyzy → FaWiersz → Rozliczenie → Platnosc → WarunkiTransakcji → Zamowienie |
| G34 | `/Faktura/Fa` | CHOICE | 0..1 | G33 SEQUENCE[1..1] | P_6 → OkresFa |
| G35 | `/Faktura/Fa/OkresFa` | SEQUENCE | 1..1 |  | P_6_Od → P_6_Do |
| G36 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] | P_13_1 → P_14_1 → P_14_1W |
| G37 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] | P_13_2 → P_14_2 → P_14_2W |
| G38 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] | P_13_3 → P_14_3 → P_14_3W |
| G39 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] | P_13_4 → P_14_4 → P_14_4W |
| G40 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] | P_13_5 → P_14_5 |
| G41 | `/Faktura/Fa/Adnotacje` | SEQUENCE | 1..1 |  | P_16 → P_17 → P_18 → P_18A → Zwolnienie → NoweSrodkiTransportu → P_23 → PMarzy |
| G42 | `/Faktura/Fa/Adnotacje/Zwolnienie` | CHOICE | 1..1 |  | Sequence → P_19N |
| G43 | `/Faktura/Fa/Adnotacje/Zwolnienie` | SEQUENCE | 1..1 | G42 CHOICE[1..1] | P_19 → Choice |
| G44 | `/Faktura/Fa/Adnotacje/Zwolnienie` | CHOICE | 1..1 | G42 CHOICE[1..1] / G43 SEQUENCE[1..1] | P_19A → P_19B → P_19C |
| G45 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu` | CHOICE | 1..1 |  | Sequence → P_22N |
| G46 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu` | SEQUENCE | 1..1 | G45 CHOICE[1..1] | P_22 → P_42_5 → NowySrodekTransportu |
| G47 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | SEQUENCE | 1..1 |  | P_22A → P_NrWierszaNST → P_22BMK → P_22BMD → P_22BK → P_22BNR → P_22BRP → Choice |
| G48 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | CHOICE | 1..1 | G47 SEQUENCE[1..1] | Sequence → Sequence → Sequence |
| G49 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | SEQUENCE | 1..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] | P_22B → Choice → P_22BT |
| G50 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | CHOICE | 0..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] / G49 SEQUENCE[1..1] | P_22B1 → P_22B2 → P_22B3 → P_22B4 |
| G51 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | SEQUENCE | 1..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] | P_22C → P_22C1 |
| G52 | `/Faktura/Fa/Adnotacje/NoweSrodkiTransportu/NowySrodekTransportu` | SEQUENCE | 1..1 | G47 SEQUENCE[1..1] / G48 CHOICE[1..1] | P_22D → P_22D1 |
| G53 | `/Faktura/Fa/Adnotacje/PMarzy` | CHOICE | 1..1 |  | Sequence → P_PMarzyN |
| G54 | `/Faktura/Fa/Adnotacje/PMarzy` | SEQUENCE | 1..1 | G53 CHOICE[1..1] | P_PMarzy → Choice |
| G55 | `/Faktura/Fa/Adnotacje/PMarzy` | CHOICE | 1..1 | G53 CHOICE[1..1] / G54 SEQUENCE[1..1] | P_PMarzy_2 → P_PMarzy_3_1 → P_PMarzy_3_2 → P_PMarzy_3_3 |
| G56 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] | PrzyczynaKorekty → TypKorekty → DaneFaKorygowanej → OkresFaKorygowanej → NrFaKorygowany → Podmiot1K → Podmiot2K → Sequence |
| G57 | `/Faktura/Fa/DaneFaKorygowanej` | SEQUENCE | 1..1 |  | DataWystFaKorygowanej → NrFaKorygowanej → Choice |
| G58 | `/Faktura/Fa/DaneFaKorygowanej` | CHOICE | 1..1 | G57 SEQUENCE[1..1] | Sequence → NrKSeFN |
| G59 | `/Faktura/Fa/DaneFaKorygowanej` | SEQUENCE | 1..1 | G57 SEQUENCE[1..1] / G58 CHOICE[1..1] | NrKSeF → NrKSeFFaKorygowanej |
| G60 | `/Faktura/Fa/Podmiot1K` | SEQUENCE | 1..1 |  | PrefiksPodatnika → DaneIdentyfikacyjne → Adres |
| G61 | `/Faktura/Fa/Podmiot1K/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | NIP → Nazwa |
| G62 | `/Faktura/Fa/Podmiot1K/Adres` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G63 | `/Faktura/Fa/Podmiot2K` | SEQUENCE | 1..1 |  | DaneIdentyfikacyjne → Adres → IDNabywcy |
| G64 | `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | Choice → Sequence |
| G65 | `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne` | CHOICE | 1..1 | G64 SEQUENCE[1..1] | NIP → Sequence → Sequence → BrakID |
| G66 | `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] | KodUE → NrVatUE |
| G67 | `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G64 SEQUENCE[1..1] / G65 CHOICE[1..1] | KodKraju → NrID |
| G68 | `/Faktura/Fa/Podmiot2K/DaneIdentyfikacyjne` | SEQUENCE | 0..1 | G64 SEQUENCE[1..1] | Nazwa |
| G69 | `/Faktura/Fa/Podmiot2K/Adres` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G70 | `/Faktura/Fa` | SEQUENCE | 0..1 | G33 SEQUENCE[1..1] / G56 SEQUENCE[0..1] | P_15ZK → KursWalutyZK |
| G71 | `/Faktura/Fa/ZaliczkaCzesciowa` | SEQUENCE | 1..1 |  | P_6Z → P_15Z → KursWalutyZW |
| G72 | `/Faktura/Fa/DodatkowyOpis` | SEQUENCE | 1..1 |  | NrWiersza → Klucz → Wartosc |
| G73 | `/Faktura/Fa/FakturaZaliczkowa` | CHOICE | 1..1 |  | Sequence → NrKSeFFaZaliczkowej |
| G74 | `/Faktura/Fa/FakturaZaliczkowa` | SEQUENCE | 1..1 | G73 CHOICE[1..1] | NrKSeFZN → NrFaZaliczkowej |
| G75 | `/Faktura/Fa/FaWiersz` | SEQUENCE | 1..1 |  | NrWierszaFa → UU_ID → P_6A → P_7 → Indeks → GTIN → PKWiU → CN → PKOB → P_8A → P_8B → P_9A → P_9B → P_10 → P_11 → P_11A → P_11Vat → P_12 → P_12_XII → P_12_Zal_15 → KwotaAkcyzy → GTU → Procedura → KursWaluty → StanPrzed |
| G76 | `/Faktura/Fa/Rozliczenie` | SEQUENCE | 1..1 |  | Obciazenia → SumaObciazen → Odliczenia → SumaOdliczen → Choice |
| G77 | `/Faktura/Fa/Rozliczenie/Obciazenia` | SEQUENCE | 1..1 |  | Kwota → Powod |
| G78 | `/Faktura/Fa/Rozliczenie/Odliczenia` | SEQUENCE | 1..1 |  | Kwota → Powod |
| G79 | `/Faktura/Fa/Rozliczenie` | CHOICE | 0..1 | G76 SEQUENCE[1..1] | DoZaplaty → DoRozliczenia |
| G80 | `/Faktura/Fa/Platnosc` | SEQUENCE | 1..1 |  | Choice → TerminPlatnosci → Choice → RachunekBankowy → RachunekBankowyFaktora → Skonto → LinkDoPlatnosci → IPKSeF |
| G81 | `/Faktura/Fa/Platnosc` | CHOICE | 0..1 | G80 SEQUENCE[1..1] | Sequence → Sequence |
| G82 | `/Faktura/Fa/Platnosc` | SEQUENCE | 1..1 | G80 SEQUENCE[1..1] / G81 CHOICE[0..1] | Zaplacono → DataZaplaty |
| G83 | `/Faktura/Fa/Platnosc` | SEQUENCE | 1..1 | G80 SEQUENCE[1..1] / G81 CHOICE[0..1] | ZnacznikZaplatyCzesciowej → ZaplataCzesciowa |
| G84 | `/Faktura/Fa/Platnosc/ZaplataCzesciowa` | SEQUENCE | 1..1 |  | KwotaZaplatyCzesciowej → DataZaplatyCzesciowej → Choice |
| G85 | `/Faktura/Fa/Platnosc/ZaplataCzesciowa` | CHOICE | 0..1 | G84 SEQUENCE[1..1] | FormaPlatnosci → Sequence |
| G86 | `/Faktura/Fa/Platnosc/ZaplataCzesciowa` | SEQUENCE | 1..1 | G84 SEQUENCE[1..1] / G85 CHOICE[0..1] | PlatnoscInna → OpisPlatnosci |
| G87 | `/Faktura/Fa/Platnosc/TerminPlatnosci` | SEQUENCE | 0..1 |  | Termin → TerminOpis |
| G88 | `/Faktura/Fa/Platnosc/TerminPlatnosci/TerminOpis` | SEQUENCE | 1..1 |  | Ilosc → Jednostka → ZdarzeniePoczatkowe |
| G89 | `/Faktura/Fa/Platnosc` | CHOICE | 0..1 | G80 SEQUENCE[1..1] | FormaPlatnosci → Sequence |
| G90 | `/Faktura/Fa/Platnosc` | SEQUENCE | 1..1 | G80 SEQUENCE[1..1] / G89 CHOICE[0..1] | PlatnoscInna → OpisPlatnosci |
| G91 | `/Faktura/Fa/Platnosc/RachunekBankowy` | SEQUENCE | 1..1 |  | NrRB → SWIFT → RachunekWlasnyBanku → NazwaBanku → OpisRachunku |
| G92 | `/Faktura/Fa/Platnosc/RachunekBankowyFaktora` | SEQUENCE | 1..1 |  | NrRB → SWIFT → RachunekWlasnyBanku → NazwaBanku → OpisRachunku |
| G93 | `/Faktura/Fa/Platnosc/Skonto` | SEQUENCE | 1..1 |  | WarunkiSkonta → WysokoscSkonta |
| G94 | `/Faktura/Fa/WarunkiTransakcji` | SEQUENCE | 1..1 |  | Umowy → Zamowienia → NrPartiiTowaru → WarunkiDostawy → Sequence → Transport → PodmiotPosredniczacy |
| G95 | `/Faktura/Fa/WarunkiTransakcji/Umowy` | SEQUENCE | 0..1 |  | DataUmowy → NrUmowy |
| G96 | `/Faktura/Fa/WarunkiTransakcji/Zamowienia` | SEQUENCE | 0..1 |  | DataZamowienia → NrZamowienia |
| G97 | `/Faktura/Fa/WarunkiTransakcji` | SEQUENCE | 0..1 | G94 SEQUENCE[1..1] | KursUmowny → WalutaUmowna |
| G98 | `/Faktura/Fa/WarunkiTransakcji/Transport` | SEQUENCE | 1..1 |  | Choice → Przewoznik → NrZleceniaTransportu → Choice → JednostkaOpakowania → Sequence |
| G99 | `/Faktura/Fa/WarunkiTransakcji/Transport` | CHOICE | 1..1 | G98 SEQUENCE[1..1] | RodzajTransportu → Sequence |
| G100 | `/Faktura/Fa/WarunkiTransakcji/Transport` | SEQUENCE | 1..1 | G98 SEQUENCE[1..1] / G99 CHOICE[1..1] | TransportInny → OpisInnegoTransportu |
| G101 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik` | SEQUENCE | 1..1 |  | DaneIdentyfikacyjne → AdresPrzewoznika |
| G102 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne` | SEQUENCE | 1..1 |  | Choice → Sequence |
| G103 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne` | CHOICE | 1..1 | G102 SEQUENCE[1..1] | NIP → Sequence → Sequence → BrakID |
| G104 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] | KodUE → NrVatUE |
| G105 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne` | SEQUENCE | 1..1 | G102 SEQUENCE[1..1] / G103 CHOICE[1..1] | KodKraju → NrID |
| G106 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/DaneIdentyfikacyjne` | SEQUENCE | 0..1 | G102 SEQUENCE[1..1] | Nazwa |
| G107 | `/Faktura/Fa/WarunkiTransakcji/Transport/Przewoznik/AdresPrzewoznika` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G108 | `/Faktura/Fa/WarunkiTransakcji/Transport` | CHOICE | 1..1 | G98 SEQUENCE[1..1] | OpisLadunku → Sequence |
| G109 | `/Faktura/Fa/WarunkiTransakcji/Transport` | SEQUENCE | 1..1 | G98 SEQUENCE[1..1] / G108 CHOICE[1..1] | LadunekInny → OpisInnegoLadunku |
| G110 | `/Faktura/Fa/WarunkiTransakcji/Transport` | SEQUENCE | 0..1 | G98 SEQUENCE[1..1] | DataGodzRozpTransportu → DataGodzZakTransportu → WysylkaZ → WysylkaPrzez → WysylkaDo |
| G111 | `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaZ` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G112 | `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaPrzez` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G113 | `/Faktura/Fa/WarunkiTransakcji/Transport/WysylkaDo` | SEQUENCE | 1..1 |  | KodKraju → AdresL1 → AdresL2 → GLN |
| G114 | `/Faktura/Fa/Zamowienie` | SEQUENCE | 1..1 |  | WartoscZamowienia → ZamowienieWiersz |
| G115 | `/Faktura/Fa/Zamowienie/ZamowienieWiersz` | SEQUENCE | 1..1 |  | NrWierszaZam → UU_IDZ → P_7Z → IndeksZ → GTINZ → PKWiUZ → CNZ → PKOBZ → P_8AZ → P_8BZ → P_9AZ → P_11NettoZ → P_11VatZ → P_12Z → P_12Z_XII → P_12Z_Zal_15 → GTUZ → ProceduraZ → KwotaAkcyzyZ → StanPrzedZ |
| G116 | `/Faktura/Stopka` | SEQUENCE | 0..1 |  | Informacje → Rejestry |
| G117 | `/Faktura/Stopka/Informacje` | SEQUENCE | 0..1 |  | StopkaFaktury |
| G118 | `/Faktura/Stopka/Rejestry` | SEQUENCE | 0..1 |  | PelnaNazwa → KRS → REGON → BDO |
| G119 | `/Faktura/Zalacznik` | SEQUENCE | 1..1 |  | BlokDanych |
| G120 | `/Faktura/Zalacznik/BlokDanych` | SEQUENCE | 1..1 |  | ZNaglowek → MetaDane → Tekst → Sequence |
| G121 | `/Faktura/Zalacznik/BlokDanych/MetaDane` | SEQUENCE | 1..1 |  | ZKlucz → ZWartosc |
| G122 | `/Faktura/Zalacznik/BlokDanych/Tekst` | SEQUENCE | 1..1 |  | Akapit |
| G123 | `/Faktura/Zalacznik/BlokDanych` | SEQUENCE | 0..1 | G120 SEQUENCE[1..1] | Tabela |
| G124 | `/Faktura/Zalacznik/BlokDanych/Tabela` | SEQUENCE | 1..1 |  | TMetaDane → Opis → TNaglowek → Wiersz → Suma |
| G125 | `/Faktura/Zalacznik/BlokDanych/Tabela/TMetaDane` | SEQUENCE | 1..1 |  | TKlucz → TWartosc |
| G126 | `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek` | SEQUENCE | 1..1 |  | Kol |
| G127 | `/Faktura/Zalacznik/BlokDanych/Tabela/TNaglowek/Kol` | SEQUENCE | 1..1 |  | NKom |
| G128 | `/Faktura/Zalacznik/BlokDanych/Tabela/Wiersz` | SEQUENCE | 1..1 |  | WKom |
| G129 | `/Faktura/Zalacznik/BlokDanych/Tabela/Suma` | SEQUENCE | 1..1 |  | SKom |

## Katalog wszystkich nazwanych typów prostych (również zależności)

Wykaz obejmuje też typy pomocnicze niewykorzystywane przez Faktura — nie wymagają tabel ani osobnego przechowywania. Built-in `xs:*` podlegają XML Schema 1.0. Listy enum są zamknięte według pobranej wersji schematu, nawet jeśli zewnętrzne słowniki zmieniły się później.

| Typ | Baza i własne ograniczenia |
|---|---|
| `etd:TAdresEmail` | etd:TAdresEmail [base=xs:token; MinLength=3; MaxLength=255; Pattern=(.)+@(.)+] |
| `etd:TCalkowity` | etd:TCalkowity [base=xs:int; WhiteSpace=collapse; TotalDigits=14] |
| `etd:TCelZlozenia` | etd:TCelZlozenia [base=xs:byte; Enumeration=1; Enumeration=2] |
| `etd:TData` | etd:TData [base=xs:date; MinInclusive=1900-01-01; MaxInclusive=2050-12-31; Pattern=((\d{4})-(\d{2})-(\d{2}))] |
| `etd:TDataCzas` | etd:TDataCzas [base=xs:dateTime; WhiteSpace=collapse] |
| `etd:TImie` | etd:TImie [base=xs:token; MinLength=1; MaxLength=30] |
| `etd:TJednAdmin` | etd:TJednAdmin [base=xs:token; MaxLength=36; MinLength=1] |
| `etd:TKodKraju` | etd:TKodKraju [base=xs:normalizedString; Enumeration=AF; Enumeration=AX; Enumeration=AL; Enumeration=DZ; Enumeration=AD; Enumeration=AO; Enumeration=AI; Enumeration=AQ; Enumeration=AG; Enumeration=AN; Enumeration=SA; Enumeration=AR; Enumeration=AM; Enumeration=AW; Enumeration=AU; Enumeration=AT; Enumeration=AZ; Enumeration=BS; Enumeration=BH; Enumeration=BD; Enumeration=BB; Enumeration=BE; Enumeration=BZ; Enumeration=BJ; Enumeration=BM; Enumeration=BT; Enumeration=BY; Enumeration=BO; Enumeration=BQ; Enumeration=BA; Enumeration=BW; Enumeration=BR; Enumeration=BN; Enumeration=IO; Enumeration=BG; Enumeration=BF; Enumeration=BI; Enumeration=XC; Enumeration=CL; Enumeration=CN; Enumeration=HR; Enumeration=CW; Enumeration=CY; Enumeration=TD; Enumeration=ME; Enumeration=DK; Enumeration=DM; Enumeration=DO; Enumeration=DJ; Enumeration=EG; Enumeration=EC; Enumeration=ER; Enumeration=EE; Enumeration=ET; Enumeration=FK; Enumeration=FJ; Enumeration=PH; Enumeration=FI; Enumeration=FR; Enumeration=TF; Enumeration=GA; Enumeration=GM; Enumeration=GH; Enumeration=GI; Enumeration=GR; Enumeration=GD; Enumeration=GL; Enumeration=GE; Enumeration=GU; Enumeration=GG; Enumeration=GY; Enumeration=GF; Enumeration=GP; Enumeration=GT; Enumeration=GN; Enumeration=GQ; Enumeration=GW; Enumeration=HT; Enumeration=ES; Enumeration=HN; Enumeration=HK; Enumeration=IN; Enumeration=ID; Enumeration=IQ; Enumeration=IR; Enumeration=IE; Enumeration=IS; Enumeration=IL; Enumeration=JM; Enumeration=JP; Enumeration=YE; Enumeration=JE; Enumeration=JO; Enumeration=KY; Enumeration=KH; Enumeration=CM; Enumeration=CA; Enumeration=QA; Enumeration=KZ; Enumeration=KE; Enumeration=KG; Enumeration=KI; Enumeration=CO; Enumeration=KM; Enumeration=CG; Enumeration=CD; Enumeration=KP; Enumeration=XK; Enumeration=CR; Enumeration=CU; Enumeration=KW; Enumeration=LA; Enumeration=LS; Enumeration=LB; Enumeration=LR; Enumeration=LY; Enumeration=LI; Enumeration=LT; Enumeration=LV; Enumeration=LU; Enumeration=MK; Enumeration=MG; Enumeration=YT; Enumeration=MO; Enumeration=MW; Enumeration=MV; Enumeration=MY; Enumeration=ML; Enumeration=MT; Enumeration=MP; Enumeration=MA; Enumeration=MQ; Enumeration=MR; Enumeration=MU; Enumeration=MX; Enumeration=XL; Enumeration=FM; Enumeration=UM; Enumeration=MD; Enumeration=MC; Enumeration=MN; Enumeration=MS; Enumeration=MZ; Enumeration=MM; Enumeration=NA; Enumeration=NR; Enumeration=NP; Enumeration=NL; Enumeration=DE; Enumeration=NE; Enumeration=NG; Enumeration=NI; Enumeration=NU; Enumeration=NF; Enumeration=NO; Enumeration=NC; Enumeration=NZ; Enumeration=PS; Enumeration=OM; Enumeration=PK; Enumeration=PW; Enumeration=PA; Enumeration=PG; Enumeration=PY; Enumeration=PE; Enumeration=PN; Enumeration=PF; Enumeration=PL; Enumeration=GS; Enumeration=PT; Enumeration=PR; Enumeration=CF; Enumeration=CZ; Enumeration=KR; Enumeration=ZA; Enumeration=RE; Enumeration=RU; Enumeration=RO; Enumeration=RW; Enumeration=EH; Enumeration=BL; Enumeration=KN; Enumeration=LC; Enumeration=MF; Enumeration=VC; Enumeration=SV; Enumeration=WS; Enumeration=AS; Enumeration=SM; Enumeration=SN; Enumeration=RS; Enumeration=SC; Enumeration=SL; Enumeration=SG; Enumeration=SK; Enumeration=SI; Enumeration=SO; Enumeration=LK; Enumeration=PM; Enumeration=US; Enumeration=SZ; Enumeration=SD; Enumeration=SS; Enumeration=SR; Enumeration=SJ; Enumeration=SH; Enumeration=SY; Enumeration=CH; Enumeration=SE; Enumeration=TJ; Enumeration=TH; Enumeration=TW; Enumeration=TZ; Enumeration=TG; Enumeration=TK; Enumeration=TO; Enumeration=TT; Enumeration=TN; Enumeration=TR; Enumeration=TM; Enumeration=TV; Enumeration=UG; Enumeration=UA; Enumeration=UY; Enumeration=UZ; Enumeration=VU; Enumeration=WF; Enumeration=VA; Enumeration=HU; Enumeration=VE; Enumeration=GB; Enumeration=VN; Enumeration=IT; Enumeration=TL; Enumeration=CI; Enumeration=BV; Enumeration=CX; Enumeration=IM; Enumeration=SX; Enumeration=CK; Enumeration=VI; Enumeration=VG; Enumeration=HM; Enumeration=CC; Enumeration=MH; Enumeration=FO; Enumeration=SB; Enumeration=ST; Enumeration=TC; Enumeration=ZM; Enumeration=CV; Enumeration=ZW; Enumeration=AE; Enumeration=XI] |
| `etd:TKodKrajuUrodzenia` | etd:TKodKrajuUrodzenia [base=xs:string; Length=2; Pattern=[A-Z]{2}] |
| `etd:TKodKrajuWydania` | etd:TKodKrajuWydania [base=etd:TKodKraju; Pattern=P[A-KM-Z]; Pattern=[A-OQ-Z][A-Z]] |
| `etd:TKodPocztowy` | etd:TKodPocztowy [base=etd:TZnakowy; MaxLength=8] |
| `etd:TKwartal` | etd:TKwartal [base=xs:byte; MinInclusive=1; MaxInclusive=4] |
| `etd:TKwota2` | etd:TKwota2 [base=xs:decimal; TotalDigits=16; WhiteSpace=collapse; FractionDigits=2] |
| `etd:TKwota2Nieujemna` | etd:TKwota2Nieujemna [base=etd:TKwota2; MinInclusive=0] |
| `etd:TKwotaC` | etd:TKwotaC [base=xs:integer; TotalDigits=14] |
| `etd:TKwotaCNieujemna` | etd:TKwotaCNieujemna [base=etd:TKwotaC; MinInclusive=0] |
| `etd:TMiejscowosc` | etd:TMiejscowosc [base=xs:token; MaxLength=56; MinLength=1] |
| `etd:TMiesiac` | etd:TMiesiac [base=xs:byte; MaxInclusive=12; MinInclusive=1] |
| `etd:TNaturalny` | etd:TNaturalny [base=xs:nonNegativeInteger; WhiteSpace=collapse; TotalDigits=14] |
| `etd:TNazwisko` | etd:TNazwisko [base=xs:token; MaxLength=81; MinLength=1] |
| `etd:TNrAKC` | etd:TNrAKC [base=xs:string; Pattern=[A-Z]{2}\d{11}] |
| `etd:TNrBudynku` | etd:TNrBudynku [base=etd:TZnakowy; MaxLength=9] |
| `etd:TNrDokumentu` | etd:TNrDokumentu [base=etd:TZnakowy; Length=5; Pattern=\d{2}/\d{2}] |
| `etd:TNrDokumentuStwierdzajacegoTozsamosc` | etd:TNrDokumentuStwierdzajacegoTozsamosc [base=xs:string; MinLength=1; MaxLength=50; WhiteSpace=replace] |
| `etd:TNrIdentyfikacjiPodatkowej` | etd:TNrIdentyfikacjiPodatkowej [base=xs:string; WhiteSpace=replace; MinLength=1; MaxLength=50] |
| `etd:TNrKRS` | etd:TNrKRS [base=xs:string; Pattern=\d{10}] |
| `etd:TNrLokalu` | etd:TNrLokalu [base=etd:TZnakowy; MaxLength=10] |
| `etd:TNrNIP` | etd:TNrNIP [base=xs:string; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}] |
| `etd:TNrPESEL` | etd:TNrPESEL [base=xs:string; Pattern=\d{11}] |
| `etd:TNrREGON` | etd:TNrREGON UNION(inline [base=xs:string; Pattern=\d{9}] OR inline [base=xs:string; Pattern=\d{14}]) |
| `etd:TProcentowy` | etd:TProcentowy [base=xs:decimal; TotalDigits=5; FractionDigits=2; MinInclusive=0; MaxInclusive=100; WhiteSpace=collapse] |
| `etd:TRok` | etd:TRok [base=xs:gYear; MinInclusive=1900; MaxInclusive=2050] |
| `etd:TRzeczywisty` | etd:TRzeczywisty [base=etd:TKwota2; MinInclusive=0] |
| `etd:TTekstowy` | etd:TTekstowy [base=xs:string; MinLength=1; MaxLength=3500] |
| `etd:TUlica` | etd:TUlica [base=etd:TZnakowy; MaxLength=65] |
| `etd:TWybor1` | etd:TWybor1 [base=xs:byte; Enumeration=1] |
| `etd:TWybor1_2` | etd:TWybor1_2 [base=xs:byte; Enumeration=1; Enumeration=2] |
| `etd:TWybor1_3` | etd:TWybor1_3 [base=xs:byte; Enumeration=1; Enumeration=2; Enumeration=3] |
| `etd:TZnakowy` | etd:TZnakowy [base=xs:token; MinLength=1; MaxLength=240] |
| `tns:SWIFT_Type` | tns:SWIFT_Type [base=xs:string; Pattern=[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3}){0,1}] |
| `tns:TData` | tns:TData [base=etd:TData; MinInclusive=2016-07-01; MaxInclusive=2050-01-01] |
| `tns:TDataCzas` | tns:TDataCzas [base=etd:TDataCzas; MinInclusive=2021-10-01T00:00:00Z; MaxInclusive=2050-01-01T23:59:59Z] |
| `tns:TDataT` | tns:TDataT [base=etd:TData; MinInclusive=2006-01-01; MaxInclusive=2050-01-01] |
| `tns:TDataU` | tns:TDataU [base=etd:TData; MinInclusive=1990-01-01; MaxInclusive=2050-01-01] |
| `tns:TFormaPlatnosci` | tns:TFormaPlatnosci [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7] |
| `tns:TGLN` | tns:TGLN [base=xs:token; MinLength=1; MaxLength=13] |
| `tns:TGTU` | tns:TGTU [base=tns:TZnakowy; Enumeration=GTU_01; Enumeration=GTU_02; Enumeration=GTU_03; Enumeration=GTU_04; Enumeration=GTU_05; Enumeration=GTU_06; Enumeration=GTU_07; Enumeration=GTU_08; Enumeration=GTU_09; Enumeration=GTU_10; Enumeration=GTU_11; Enumeration=GTU_12; Enumeration=GTU_13] |
| `tns:TIlosci` | tns:TIlosci [base=xs:decimal; TotalDigits=22; FractionDigits=6; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,6})?] |
| `tns:TKodFormularza` | tns:TKodFormularza [base=xs:string; Enumeration=FA] |
| `tns:TKodWaluty` | tns:TKodWaluty [base=xs:normalizedString; Enumeration=AED; Enumeration=AFN; Enumeration=ALL; Enumeration=AMD; Enumeration=ANG; Enumeration=AOA; Enumeration=ARS; Enumeration=AUD; Enumeration=AWG; Enumeration=AZN; Enumeration=BAM; Enumeration=BBD; Enumeration=BDT; Enumeration=BGN; Enumeration=BHD; Enumeration=BIF; Enumeration=BMD; Enumeration=BND; Enumeration=BOB; Enumeration=BOV; Enumeration=BRL; Enumeration=BSD; Enumeration=BTN; Enumeration=BWP; Enumeration=BYN; Enumeration=BZD; Enumeration=CAD; Enumeration=CDF; Enumeration=CHE; Enumeration=CHF; Enumeration=CHW; Enumeration=CLF; Enumeration=CLP; Enumeration=CNY; Enumeration=COP; Enumeration=COU; Enumeration=CRC; Enumeration=CUC; Enumeration=CUP; Enumeration=CVE; Enumeration=CZK; Enumeration=DJF; Enumeration=DKK; Enumeration=DOP; Enumeration=DZD; Enumeration=EGP; Enumeration=ERN; Enumeration=ETB; Enumeration=EUR; Enumeration=FJD; Enumeration=FKP; Enumeration=GBP; Enumeration=GEL; Enumeration=GGP; Enumeration=GHS; Enumeration=GIP; Enumeration=GMD; Enumeration=GNF; Enumeration=GTQ; Enumeration=GYD; Enumeration=HKD; Enumeration=HNL; Enumeration=HRK; Enumeration=HTG; Enumeration=HUF; Enumeration=IDR; Enumeration=ILS; Enumeration=IMP; Enumeration=INR; Enumeration=IQD; Enumeration=IRR; Enumeration=ISK; Enumeration=JEP; Enumeration=JMD; Enumeration=JOD; Enumeration=JPY; Enumeration=KES; Enumeration=KGS; Enumeration=KHR; Enumeration=KMF; Enumeration=KPW; Enumeration=KRW; Enumeration=KWD; Enumeration=KYD; Enumeration=KZT; Enumeration=LAK; Enumeration=LBP; Enumeration=LKR; Enumeration=LRD; Enumeration=LSL; Enumeration=LYD; Enumeration=MAD; Enumeration=MDL; Enumeration=MGA; Enumeration=MKD; Enumeration=MMK; Enumeration=MNT; Enumeration=MOP; Enumeration=MRU; Enumeration=MUR; Enumeration=MVR; Enumeration=MWK; Enumeration=MXN; Enumeration=MXV; Enumeration=MYR; Enumeration=MZN; Enumeration=NAD; Enumeration=NGN; Enumeration=NIO; Enumeration=NOK; Enumeration=NPR; Enumeration=NZD; Enumeration=OMR; Enumeration=PAB; Enumeration=PEN; Enumeration=PGK; Enumeration=PHP; Enumeration=PKR; Enumeration=PLN; Enumeration=PYG; Enumeration=QAR; Enumeration=RON; Enumeration=RSD; Enumeration=RUB; Enumeration=RWF; Enumeration=SAR; Enumeration=SBD; Enumeration=SCR; Enumeration=SDG; Enumeration=SEK; Enumeration=SGD; Enumeration=SHP; Enumeration=SLL; Enumeration=SOS; Enumeration=SRD; Enumeration=SSP; Enumeration=STN; Enumeration=SVC; Enumeration=SYP; Enumeration=SZL; Enumeration=THB; Enumeration=TJS; Enumeration=TMT; Enumeration=TND; Enumeration=TOP; Enumeration=TRY; Enumeration=TTD; Enumeration=TWD; Enumeration=TZS; Enumeration=UAH; Enumeration=UGX; Enumeration=USD; Enumeration=USN; Enumeration=UYI; Enumeration=UYU; Enumeration=UYW; Enumeration=UZS; Enumeration=VES; Enumeration=VND; Enumeration=VUV; Enumeration=WST; Enumeration=XAF; Enumeration=XAG; Enumeration=XAU; Enumeration=XBA; Enumeration=XBB; Enumeration=XBC; Enumeration=XBD; Enumeration=XCD; Enumeration=XCG; Enumeration=XDR; Enumeration=XOF; Enumeration=XPD; Enumeration=XPF; Enumeration=XPT; Enumeration=XSU; Enumeration=XUA; Enumeration=XXX; Enumeration=YER; Enumeration=ZAR; Enumeration=ZMW; Enumeration=ZWL] |
| `tns:TKodyKrajowUE` | tns:TKodyKrajowUE [base=xs:normalizedString; Enumeration=AT; Enumeration=BE; Enumeration=BG; Enumeration=CY; Enumeration=CZ; Enumeration=DK; Enumeration=EE; Enumeration=FI; Enumeration=FR; Enumeration=DE; Enumeration=EL; Enumeration=HR; Enumeration=HU; Enumeration=IE; Enumeration=IT; Enumeration=LV; Enumeration=LT; Enumeration=LU; Enumeration=MT; Enumeration=NL; Enumeration=PL; Enumeration=PT; Enumeration=RO; Enumeration=SK; Enumeration=SI; Enumeration=ES; Enumeration=SE; Enumeration=XI] |
| `tns:TKwotowy` | tns:TKwotowy [base=xs:decimal; TotalDigits=18; FractionDigits=2; Pattern=-?([1-9]\d{0,15}&#124;0)(\.\d{1,2})?] |
| `tns:TKwotowy2` | tns:TKwotowy2 [base=xs:decimal; TotalDigits=22; FractionDigits=8; Pattern=-?([1-9]\d{0,13}&#124;0)(\.\d{1,8})?] |
| `tns:TLadunek` | tns:TLadunek [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7; Enumeration=8; Enumeration=9; Enumeration=10; Enumeration=11; Enumeration=12; Enumeration=13; Enumeration=14; Enumeration=15; Enumeration=16; Enumeration=17; Enumeration=18; Enumeration=19; Enumeration=20] |
| `tns:TNaturalny` | tns:TNaturalny [base=etd:TNaturalny; MinExclusive=0] |
| `tns:TNIPIdWew` | tns:TNIPIdWew [base=tns:TZnakowy20; Pattern=[1-9]((\d[1-9])&#124;([1-9]\d))\d{7}-\d{5}] |
| `tns:TNrRB` | tns:TNrRB [base=xs:string; MinLength=10; MaxLength=34] |
| `tns:TNrVatUE` | tns:TNrVatUE [base=xs:string; Pattern=(\d&#124;[A-Z]&#124;\+&#124;\*){1,12}] |
| `tns:TNumerKSeF` | tns:TNumerKSeF [base=xs:token; Pattern=([1-9]((\d[1-9])&#124;([1-9]\d))\d{7}&#124;M\d{9}&#124;[A-Z]{3}\d{7})-(20[2-9][0-9]&#124;2[1-9][0-9]{2}&#124;[3-9][0-9]{3})(0[1-9]&#124;1[0-2])(0[1-9]&#124;[1-2][0-9]&#124;3[0-1])-([0-9A-F]{6})-?([0-9A-F]{6})-([0-9A-F]{2})] |
| `tns:TNumerTelefonu` | tns:TNumerTelefonu [base=tns:TZnakowy; MaxLength=16] |
| `tns:TOznaczenieProcedury` | tns:TOznaczenieProcedury [base=tns:TZnakowy; Enumeration=WSTO_EE; Enumeration=IED; Enumeration=TT_D; Enumeration=I_42; Enumeration=I_63; Enumeration=B_SPV; Enumeration=B_SPV_DOSTAWA; Enumeration=B_MPV_PROWIZJA] |
| `tns:TOznaczenieProceduryZ` | tns:TOznaczenieProceduryZ [base=tns:TZnakowy; Enumeration=WSTO_EE; Enumeration=IED; Enumeration=TT_D; Enumeration=B_SPV; Enumeration=B_SPV_DOSTAWA; Enumeration=B_MPV_PROWIZJA] |
| `tns:TProcentowy` | tns:TProcentowy [base=xs:decimal; TotalDigits=9; FractionDigits=6; MinInclusive=0; MaxInclusive=100; WhiteSpace=collapse] |
| `tns:TRachunekWlasnyBanku` | tns:TRachunekWlasnyBanku [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] |
| `tns:TRodzajFaktury` | tns:TRodzajFaktury [base=tns:TZnakowy; Enumeration=VAT; Enumeration=KOR; Enumeration=ZAL; Enumeration=ROZ; Enumeration=UPR; Enumeration=KOR_ZAL; Enumeration=KOR_ROZ] |
| `tns:TRodzajTransportu` | tns:TRodzajTransportu [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=7; Enumeration=8] |
| `tns:TRolaPodmiotu3` | tns:TRolaPodmiotu3 [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4; Enumeration=5; Enumeration=6; Enumeration=7; Enumeration=8; Enumeration=9; Enumeration=10; Enumeration=11] |
| `tns:TRolaPodmiotuUpowaznionego` | tns:TRolaPodmiotuUpowaznionego [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] |
| `tns:TStatusInfoPodatnika` | tns:TStatusInfoPodatnika [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3; Enumeration=4] |
| `tns:TStawkaPodatku` | tns:TStawkaPodatku [base=tns:TZnakowy; Enumeration=23; Enumeration=22; Enumeration=8; Enumeration=7; Enumeration=5; Enumeration=4; Enumeration=3; Enumeration=0 KR; Enumeration=0 WDT; Enumeration=0 EX; Enumeration=zw; Enumeration=oo; Enumeration=np I; Enumeration=np II] |
| `tns:TTypKorekty` | tns:TTypKorekty [base=xs:integer; Enumeration=1; Enumeration=2; Enumeration=3] |
| `tns:TZnakowy` | tns:TZnakowy [base=xs:token; MinLength=1; MaxLength=256] |
| `tns:TZnakowy2` | tns:TZnakowy2 [base=xs:token; MinLength=0; MaxLength=256] |
| `tns:TZnakowy20` | tns:TZnakowy20 [base=xs:token; MinLength=1; MaxLength=20] |
| `tns:TZnakowy50` | tns:TZnakowy50 [base=xs:token; MinLength=1; MaxLength=50] |
| `tns:TZnakowy512` | tns:TZnakowy512 [base=xs:token; MinLength=1; MaxLength=512] |
| `xs:integer` | xs:integer [base=xs:decimal; ] |
| `xs:string` | xs:string [base=xs:anySimpleType; ] |

Liczba wierszy mapowania (elementy + atrybuty + treści): 411. Liczba grup: 129. Liczba nazwanych typów prostych: 78. Wszystkie ścieżki otrzymały mapowanie; typy nieosiągalne z Faktura mają wyłącznie charakter zależności XSD.
