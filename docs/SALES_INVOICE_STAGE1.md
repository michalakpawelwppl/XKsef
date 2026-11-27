# SalesInvoice — etap 1

Stan: 27.11.2026. Implementacja dotyczy projektów zwykłych faktur VAT. Nie uruchamia zatwierdzania, numeracji, KSeF, eksportu XML, React ani generatora PDF.

## Zakres i model

Nowy agregat w `XKsef.Domain/SalesInvoices` jest niezależny od `StoredInvoice`. Zawiera trwałe identyfikatory pozycji, snapshot sprzedawcy i nabywcy, sumy podatkowe, dane płatności oraz historię kursów. Typy rodzajów i statusów przewidują późniejszy obieg i inne dokumenty, ale API etapu 1 tworzy/aktualizuje wyłącznie Draft zwykłego VAT. Approved/Issued nie można zmieniać komendą aktualizacji Draft.

Przyszłe powiązania zaliczek/korekt mają osobną encję referencji, ze złożonymi FK uwzględniającymi TenantId. Jest to przygotowanie struktury, bez operacji biznesowych rozliczania. Moduł rozrachunków będzie mógł wskazać `(TenantId, SalesInvoiceId)` lub trwałe Id pozycji. Dane płatności dokumentu nie są księgą wpłat.

Jednostka i cena są snapshotem dokumentu, nie nawigacją do przyszłej kartoteki towarów. `Unit` opisuje jednostkę pozycji; `ProductCode`, GTIN, PKWiU, CN i GTU są dodatkowymi danymi pozycji. Przelicznik jednostek i ilość magazynowa mogą zostać dodane jako osobny snapshot; w tym etapie nie są obliczane. Skali ilości fakturowej nie należy narzucać przyszłemu magazynowi.

Sprzedawca domyślnie pochodzi z TaxEntity tenanta. Można uzupełnić jego dane w projekcie, ale nie podmienić NIP na cudzy. Brak pełnego profilu własnej firmy nadal wymaga uzupełniania adresu. Pierwszy wybór kontrahenta kopiuje jego dane z tenantowej kartoteki. Kolejny GET/PUT nie odświeża ich automatycznie; ponowny zapis przesłanego snapshotu zachowuje historyczną nazwę. Nie powstaje nowy endpoint odświeżania kartoteki.

## Reguły obliczeń

Wszystkie kwoty/ceny/kursy są decimal. Cena wejściowa zachowuje 8 miejsc, ilość 6. Mnożenie ilości i ceny poprzedza jakiekolwiek groszowe zaokrąglenie. Rabat procentowy pozycji nie jest wcześniej zaokrąglany do 2 lub 8 miejsc. Rabat kwotowy pozycji dotyczy całego wiersza w podstawie jego ceny.

`R2` oznacza zaokrąglenie do 2 miejsc przez AwayFromZero. Dane zwykłej sprzedaży są nieujemne; szczegółowe reguły ujemnych korekt pozostają na później.

| VatMethod | Kod | Obliczenie |
|---|---:|---|
| PerLine | 0 | Dla ceny netto: N=R2(Nraw), V=R2(N*r/100), G=N+V. Dla brutto: G=R2(Graw), V=R2(G*r/(100+r)), N=G−V |
| ByNetTotals | 1 | N pozycji zaokrąglone, V koszyka=R2(sum(N)*r/100), następnie podział VAT na pozycje |
| ByGrossTotals | 2 | G pozycji zaokrąglone, V koszyka=R2(sum(G)*r/(100+r)), następnie podział VAT i N=G−V |

Cena źródłowa Net/Gross (kody 0/1) jest niezależna od metody dokumentu. W metodzie grupowej rozstrzygająca jest jej podstawa. Dlatego zmiana metody może zmienić końcowe netto/brutto o grosz, ale nigdy nie przepisuje ceny wejściowej.

### Rabat dokumentu

- PerLine: użytkownik wybiera Net lub Gross.
- ByNetTotals: podstawa automatycznie Net.
- ByGrossTotals: podstawa automatycznie Gross.

Przy PUT ze zmianą metody podstawa zostaje dostosowana, a dokument przeliczony w tej samej operacji. Wartość rabatu kwotowego jest interpretowana w nowej podstawie; nie jest automatycznie przeliczana netto↔brutto, ponieważ na dokumencie może być wiele stawek. Cena wejściowa i jej podstawa pozostają zachowane. Powrót do PerLine zachowuje aktualną podstawę, którą użytkownik może następnie wybrać jawnie.

Po rabatach pozycji przy dodatnim rabacie globalnym wyznaczamy groszowe wagi w wybranej podstawie Net/Gross. Kwota globalna procentowa to R2(procent * suma wag), kwotowa jest podawana w groszach. Podział proporcjonalny zachowuje dokładnie tę kwotę. Po jego odjęciu wartości w podstawie rabatu stają się pieniężną podstawą dalszych obliczeń. Także PerLine liczy wtedy podatek każdego wiersza w podstawie zastosowanego rabatu, zachowując wejściowe ceny i ich flagi Net/Gross. Powtórna konwersja do podstawy ceny źródłowej mogłaby zgubić lub przywrócić grosz rabatu. Rabat zerowy albo zaokrąglony do zera nie uruchamia tej konwersji.

Nie stosujemy przeciwnej podstawy rabatu w metodach grupowych. Rabat wyższy od podstawy jest błędem; dodatni rabat przy zerowej podstawie również. Jeden rabat pozycji i jeden dokumentu mogą być procentowe lub kwotowe (DiscountKind: 0/1).

### Determinizm i podział groszy

Wagi rabatu dzielimy proporcjonalnie do jego groszowego celu. Dla VAT i VAT PLN bierzemy części całkowite groszy wartości niezaokrąglonych ze wzorów, a pozostałe grosze przydzielamy według największych reszt. Remisy rozstrzyga rosnący tekst `LineId.ToString("N")` z porównaniem Ordinal. Kolejność wyświetlania, przetwarzania i rekordów SQL nie wpływa na wynik. Suma VAT pozycji jest zawsze równa VAT koszyka, a netto+VAT=brutto.

Do dokładnego porównania reszt i kontroli reprezentowalności mnożenia służy wbudowany BigInteger na współczynnikach/liczbach groszy. Nie zastępuje on typu kwot, nie używamy double/float i nie dodano pakietu. Chroni to przed przepełnieniem iloczynu udziałów oraz błędami remisów dzielenia. Mnożenie decimal, które po cichu redukuje istotną skalę, zostaje odrzucone jako ArithmeticRange zamiast zaokrąglać dane bez kontroli. Dzielenie nieokresowych rozwinięć nadal podlega skończonej precyzji decimal.

### Kursy i PLN

Etap 1 przyjmuje jeden ręcznie dostarczony kurs dokumentu. Model ma kolekcję niezmiennych snapshotów z kierunkiem, jednostką notowania, datą, zadeklarowanym źródłem, referencją źródłową, regułą wyboru daty i metadanymi ręcznej zmiany. Źródło nie jest weryfikowane przez bank. Żaden dostawca NBP/EBC nie jest wywoływany.

Zmiana kursu/dat/metadanych zapisuje nowy snapshot, poprzedni zostaje historyczny. Powtórne przeliczenie bez zmiany danych nie dopisuje snapshotu. `OriginalSnapshotId` wiąże zmianę z poprzednią wersją w tym agregacie. Kursy są przechowywane do 12 miejsc, a jednostka notowania do 6. Waluta docelowa to PLN; PLN nie wymaga kursu. Kurs i jednostka muszą być dodatnie.

VAT PLN = R2(VAT koszyka * Rate / QuotedUnits). Dopiero ten wynik dzielimy między pozycje według ich niezaokrąglonych wkładów PLN. Historyczne kursy korekt i wielokursowe zdarzenia będą wymagać dodatkowej strategii aplikacyjnej; nie wolno ich sprowadzać do średniego kursu dokumentu.

### Przykłady referencyjne

| Wejście | Wynik |
|---|---|
| 1250 × 100,12345678 netto | 125154,32 netto; bez wcześniejszego skrócenia ceny |
| 1,25 × 100123,45678000 netto | Ten sam wynik 125154,32 |
| 999,999999 × 0,12345678 netto | 123,46 netto zamiast błędnego 120,00 przy cenie skróconej do 0,12 |
| 2,345678 × 12,34567891 netto, rabat 12,5% | 25,34 netto, 5,83 VAT, 31,17 brutto |
| Dwie pozycje netto 0,03, VAT 23%, PerLine | N=0,06; V=0,02; G=0,08 |
| Te same dane, ByNetTotals | N=0,06; V=0,01; G=0,07 |
| Te same dane, ByGrossTotals | N=0,07; V=0,01; G=0,08 |
| VAT 3 × 0,01 EUR, kurs 4,321234 | 0,13 PLN, podział 0,05+0,04+0,04 |

## Zapis EF i migracja

Migracja `20261127075952_AddSalesInvoiceDrafts` dodaje wyłącznie siedem nowych tabel: SalesInvoices, SalesInvoiceLines, SalesInvoiceParties, SalesInvoiceTaxSummaries, SalesInvoiceReferences, SalesInvoiceDetails, SalesInvoiceExchangeRates. Nie zmienia tabel importowanych faktur, kartotek ani danych historycznych. Nie została zastosowana do aktywnej bazy użytkownika.

| Dane | SQL |
|---|---|
| Cena | decimal(22,8) |
| Ilość | decimal(22,6) |
| Kwoty wynikowe | decimal(18,2) |
| Rate / OriginalRate | decimal(28,12) |
| QuotedUnits | decimal(22,6) |
| Dane stron, płatności, wejściowego rabatu | Wersjonowane/typowane nvarchar(max), decimal w JSON jako tekst |
| Daty / audyt | date / datetimeoffset |
| Współbieżność | rowversion + Revision |

Klucze GUID tworzy domena; EF ma ValueGeneratedNever, aby nowe dzieci w istniejącym agregacie były INSERT, nie UPDATE. Każde dziecko ma TenantId i złożony FK do właściciela. Numery pozycji mają unikalny indeks tenant/faktura/Ordinal. Zmiana kolejności odbywa się z przejściowym przesunięciem numerów w tej samej transakcji co zapis agregatu; kontrola rowversion chroni całą operację i rollback obejmuje także przesunięcie. XML/KSeF/ostateczne numery nie są przechowywane jako fikcyjne wartości.

## API

Wspólny prefiks: `/api/sales-invoices`. Obowiązują istniejące JWT lub cookie, kontrola `X-Tenant-Id` i CSRF dla cookie. Etap 1 korzysta z członkostwa w aktywnym tenantcie tak jak kartoteka kontrahentów; nie dodaje polityki zatwierdzania.

| Metoda i ścieżka | Kontrakt |
|---|---|
| POST / | SalesInvoiceDraftDto → 201, dokument i Location |
| GET /{id} | 200 dokument albo 404 w bieżącym tenantcie |
| PUT /{id} | `{ "version": "Base64", "draft": {...} }` → zaktualizowany dokument |
| GET /?page=1&pageSize=50 | Wyłącznie Draft, paginacja (pageSize 1–100), projekcja bez pełnych snapshotów |
| POST /{id}/recalculate | `{ "version": "Base64" }` → nowa revision i przeliczone wartości |

Kwoty, ceny, ilości i kursy JSON są ciągami dziesiętnymi, np. `"100.12345678"`. Liczby zmiennoprzecinkowe JSON, wykładniki i wejścia tracące precyzję decimal są odrzucane. Kody enumów są liczbami całkowitymi. Nie zmieniono globalnej serializacji istniejącego API.

Minimalny projekt to `{}`. Przykład finansowo kompletnego wejścia:

```json
{
  "currency": "PLN",
  "vatMethod": 1,
  "globalDiscountBasis": 1,
  "globalDiscount": { "kind": 0, "value": "10" },
  "lines": [
    { "description": "Usługa", "unit": "szt.", "quantity": "1.25", "unitPrice": "100.12345678", "priceBasis": 0, "taxCode": "23" }
  ]
}
```

Wynik ma globalDiscountBasis=0, ponieważ metoda 1 wymusza netto. Nowa pozycja ma Id pominięte lub Guid.Empty; backend nadaje Id. PUT zachowuje istniejące Id, pominięte pozycje usuwa, nowe ponownie mają Guid.Empty. Id cudzej albo nieznanej pozycji nie można przekazać jako własnego. Wersja musi zawierać 8 bajtów rowversion zakodowanych Base64.

Odpowiedź zawiera wejściowy draft, przechowane sumy, wyniki pozycji, koszyki, historię kursów, revision i version. GET nie przelicza dokumentu nowym kalkulatorem; odczytuje wyniki i ustala jedynie brakujące dane wejściowe. Odczyt split-query weryfikuje rowversion po pobraniu dzieci i ponawia próbę po równoczesnym zapisie; nie zwraca nagłówka i pozycji z różnych revision. Sumy są nullable, gdy brakuje danych finansowych. CalculationComplete dotyczy obliczeń, a nie gotowości podatkowej/zatwierdzenia: projekt może nie mieć pełnego adresu, nabywcy lub daty. Do czasu kompletności obliczeń nie należy używać pól przydziałów/sumy rabatu jako ostatecznych.

Błędy 400 mają ProblemDetails/ValidationProblemDetails i ścieżki pól; 409 oznacza konflikt wersji albo niedozwolony stan. 404 nie ujawnia cudzej faktury. TenantId, status i wiążące wyniki nie są danymi wejściowymi projektu. Nieprawidłowe wejście nie zapisuje części dokumentu.

## Ograniczenia FA(3) i dalsze prace

Implementacja nie jest eksporterem ani pełnym walidatorem podatkowym. Wstępna walidacja używa skal i słowników z analizowanego XSD 13775 / 1-0E, ale nie oznacza zgodności wszystkich danych Draft z FA(3).

Do dalszej analizy przed eksportem pozostają:

- dobór P_9A/P_9B, P_11/P_11A i P_11Vat dla mieszanych podstaw oraz grupowego VAT;
- reprezentacja ceny pochodnej do 8 miejsc, zwłaszcza przy dużej ilości;
- odwzorowanie rozdzielonego rabatu globalnego na P_10 bez podwójnego odjęcia;
- kursy źródłowe/wewnętrzne do 12 miejsc wobec maksymalnie 6 miejsc pól kursowych FA(3); bez cichego przeliczenia historycznego VAT PLN;
- własne strategie zaliczek, ROZ, korekt, OSS i marży oraz wymagania prawne wybranej transakcji;
- automatyczny wybór właściwej daty kursu i dostawcy, wykraczający poza ręczne dane tego etapu.

## Weryfikacja i pliki

Testy kalkulatora sprawdzają również permutacje danych, precyzję, odmienne wyniki metod, rabaty i podział PLN. Testy SQL/API używają wyłącznie istniejącej fabryki izolowanego LocalDB `xKsef_Tests_<GUID>`, prawdziwych FK, rowversion, migracji, JWT i CSRF. Hosty testowe nie rejestrują klienta KSeF ani jego warmupu. Wyniki uruchomień są w `.test-results` (ignorowane przez Git).

Weryfikacja 27.11.2026:

- Przed implementacją: 1454 zaliczone, 13 pominiętych, 0 błędów.
- Po implementacji: 1531 zaliczonych, 13 pominiętych, 0 błędów; łącznie 1544 przypadki, w tym 77 nowych przypadków modułu.
- Polecenie końcowej regresji: `dotnet test xKsef.Tests/xKsef.Tests.csproj --no-build --no-restore -m:1 -nr:false --verbosity minimal --logger 'trx;LogFileName=stage1-final.trx' --results-directory .test-results`. Wcześniejszy przebieg obejmował kompilację.
- Pominięcia to istniejące testy `GusQuotaSqlServerTests` wymagające osobnego włączenia. W pierwszym przebiegu wystąpił przejściowy błąd istniejącego testu schowka WPF; ponowienie obu jego wariantów oraz cała końcowa regresja przeszły.
- Test modelu potwierdza brak oczekujących zmian względem migracji. Test migracji dopuszcza wyłącznie tworzenie siedmiu nowych tabel i indeksów; nie ma operacji zmiany istniejących tabel ani danych.
- `git diff --check` nie wykazuje błędów formatowania.

Dodane pliki: `XKsef.Domain/SalesInvoices/{SalesInvoiceTypes,SalesInvoice,SalesInvoiceValidator}.cs`, `Calculation/{FinancialMath,SalesInvoiceCalculator,InvoiceCalculationResult}.cs`; `xKesf.Application/SalesInvoices/{ISalesInvoiceRepository,SalesInvoiceService,SalesInvoiceContractMapper}.cs`; `xKsef.Contracts/SalesInvoices/SalesInvoiceContracts.cs`; kontroler i handler API; konfiguracja EF oraz repozytorium SalesInvoice; migracja i jej Designer; testy Domain/SalesInvoiceCalculationTests, Integration/SalesInvoiceTests, Infrastructure/SalesInvoiceModelTests; ten dokument.

Zmodyfikowane istniejące pliki: AppDbContext, DependencyInjection, Program, AppDbContextModelSnapshot; BrowserAuthenticationFixture (rejestracja handlera i sprzątanie nowych danych); StoredInvoiceModelAndMigrationTests i RegistrationSqlServerTests (aktualizacja oczekiwanej historii migracji); `.gitignore`; raport analizy (informacja o zatwierdzonym uzupełnieniu). Kod importu, poświadczeń, WPF i React nie został zmieniony. Nie wykonano commita ani push.
