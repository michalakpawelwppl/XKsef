# Oficjalne źródła analizy FA(3)

Pobrano 25.11.2026. Katalog jest materiałem dokumentacyjnym do [raportu](../../SALES_INVOICE_PREIMPLEMENTATION_ANALYSIS.md), nie zasobem uruchomieniowym aplikacji. Żaden plik nie został zarejestrowany w aplikacji ani użyty do połączenia z API KSeF. Oficjalne materiały zachowano w pobranej postaci; schematy nie mają przepisanych schemaLocation.

## Pochodzenie i wersje

| Plik | Źródło | Wersja / rola |
|---|---|---|
| [schemat.xsd](schemat.xsd) | [CRD](https://crd.gov.pl/wzor/2025/06/25/13775/schemat.xsd) | FA(3), wzór 13775 z 25.06.2025, kod FA (3), wersja 1-0E |
| [StrukturyDanych_v10-0E.xsd](StrukturyDanych_v10-0E.xsd) | [CRD](https://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/StrukturyDanych_v10-0E.xsd) | Import schematu FA(3) |
| [ElementarneTypyDanych_v10-0E.xsd](ElementarneTypyDanych_v10-0E.xsd) | [CRD](https://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/ElementarneTypyDanych_v10-0E.xsd) | Include typów bazowych |
| [KodyKrajow_v10-0E.xsd](KodyKrajow_v10-0E.xsd) | [CRD](https://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/KodyKrajow_v10-0E.xsd) | Include słownika krajów; domyka graf zależności |
| [broszura-fa3.pdf](broszura-fa3.pdf) | [MF](https://ksef.podatki.gov.pl/media/nuclnu0w/broszura-informacyjna-dotyczaca-struktury-logicznej-fa-3.pdf) | Sierpień 2026, 173 strony; aktualny odsyłacz strony pobrań MF |
| [podrecznik-ksef-cz-ii-2026-08-06.pdf](podrecznik-ksef-cz-ii-2026-08-06.pdf) | [MF](https://ksef.podatki.gov.pl/media/rronoxyt/podrecznik-ksef-20-cz-ii-wystawianie-i-otrzymywanie-faktur-w-ksef-06082026.pdf) | 06.08.2026, wystawianie i otrzymywanie, 106 stron |
| [podrecznik-ksef-cz-iii-2026-11-06.pdf](podrecznik-ksef-cz-iii-2026-11-06.pdf) | [MF](https://ksef.podatki.gov.pl/media/pr2dfos2/podrecznik-ksef-20-cz-iii-dodatkowe-funkcjonalnosci-ksef-06112026.pdf) | 06.11.2026, dodatkowe funkcjonalności, 103 strony; użyto części dotyczących FA(3) |
| [przyklady-fa3.zip](przyklady-fa3.zip) | [MF](https://ksef.podatki.gov.pl/media/e5cia0ey/przykladowe-pliki-dla-struktury-logicznej-e-faktury-fa-3.zip) | Oficjalne przykłady struktury FA(3), 26 XML |
| [weryfikacja-faktury-mf.txt](weryfikacja-faktury-mf.txt) | [CIRFMF raw](https://raw.githubusercontent.com/CIRFMF/ksef-api/main/faktury/weryfikacja-faktury.md) | Data w dokumencie 09.04.2026; snapshot main z dnia pobrania; hash poniżej identyfikuje dokładną treść |

Nazwy lokalne plików PDF/ZIP/tekstu zostały uproszczone. Nie zmieniono ich treści. Linki względne wewnątrz pliku weryfikacja-faktury-mf.txt odnoszą się do oryginalnego repozytorium CIRFMF. Materiały źródłowe zachowują własne rozróżnienie daty wydania i stanu prawnego. Podręcznik cz. III zawiera również zagadnienia poza FA(3); jego części FA_RR nie były przedmiotem analizy.

## Weryfikacja offline

- Cztery XSD: odczyt poprawnego XML, domknięcie wszystkich import/include przez nazwę pobranego pliku, kompilacja w .NET `System.Xml.Schema.XmlSchemaSet`: **OK**.
- Importy/include zarejestrowano jako cztery lokalne schematy; odwołania pobierania usunięto wyłącznie z obiektów w pamięci przed kompilacją, `XmlResolver=null`. Źródłowe bajty pozostały bez zmian.
- Wszystkie 26 plików XML z oryginalnego ZIP zwalidowano strumieniowo na tym zestawie, bez wypakowywania i bez sieci: **26 poprawnych, 0 błędów**. [Pełna lista wyników](EXAMPLE_VALIDATION.md).
- Przejście skompilowanego korzenia Faktura: **406 wystąpień elementów**, **3 atrybuty**, **2 treści simpleContent** — razem **411 wierszy mapowania**, **129 grup** SEQUENCE/CHOICE. Słownik typów ma **76 typów nazwanych ze źródeł i 2 typy wbudowane**. [Załącznik A](../FA3_ELEMENT_MAPPING.md).
- Kontrola dokumentacji: unikalność ścieżek mapowania, kompletność przypisania do modelu, zgodność liczby elementów drzewa i tabeli, istnienie lokalnych odsyłaczy raportu i załączników.
- Nie wykonywano testów aplikacji, kompilacji rozwiązania, połączeń do baz ani API KSeF. Walidacja przykładów jest kontrolą XSD i nie stanowi testu poprawności kalkulatora, modelu EF ani wysyłki.

Do ponowienia kontroli XSD wystarczą wbudowane biblioteki .NET System.Xml i System.IO.Compression; nie są potrzebne nowe pakiety. Wczytać cztery XSD z DtdProcessing=Prohibit, zarejestrować je w XmlSchemaSet bez pobierania zewnętrznych URI, skompilować i przejść każdy XML w ZIP przez XmlReader z ValidationType.Schema. Nie uruchamiać przy tym hosta xKsef.Api, który może wykonywać warmup KSeF.

## SHA-256 pobranych źródeł

Hash dotyczy bajtów pobranego pliku, nie tekstu po normalizacji końców linii. Pliki raportu i wyników walidacji nie należą do źródeł MF i nie są objęte poniższą tabelą.

| Plik | Bajty | SHA-256 |
|---|---:|---|
| broszura-fa3.pdf | 3597883 | a1ac223ce47a4047380337457a63e7f324180c75f2aa25004d0cc1bf4f6a7b6b |
| ElementarneTypyDanych_v10-0E.xsd | 12228 | 8a531cb181d3e298d11b28766655ae91fee2d7851440095932ffc82137ed2be1 |
| KodyKrajow_v10-0E.xsd | 40729 | 1d41a1b3184188f2d20a51d3afde26204dda182ec5dacf018204dcc9870dc644 |
| podrecznik-ksef-cz-ii-2026-08-06.pdf | 1861961 | eff14bd573271cea0b2dea8283c9fb54bad3e3261c4abf0168f993cd37b90db0 |
| podrecznik-ksef-cz-iii-2026-11-06.pdf | 3649967 | ae5d0b2d83247b4456c347083edc98026b9803bf4094c6e212f36cfc5965415a |
| przyklady-fa3.zip | 200512 | 41ebd3c57144951c65d68a36fbe433285b5791a86a8bd46cb059503e3f8b1e10 |
| schemat.xsd | 183798 | b646b6b525f51adf1bb2545f111fc8ca6e7aa6dd2f98948f1667d3695c06d958 |
| StrukturyDanych_v10-0E.xsd | 31547 | 1137ce6e3c11c2b9ef3f05e4e72d6dcd6b4fa94908ea558f2ba15de0259bb2aa |
| weryfikacja-faktury-mf.txt | 4439 | bfc616349151d379214895fbacf448906bbb825e4720a9666a161a7acd1ebf78 |
