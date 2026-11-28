# 4B.6B — rachunki sprzedawcy i faktora

## Źródła i ustalenia

Podstawą implementacji jest lokalny [schemat FA(3)](fa3-sources/schemat.xsd), wzór `13775`, oraz [broszura MF](fa3-sources/broszura-fa3.pdf), strony 111 i 116–118 (numeracja drukowana), tabele 56 i 57. W badanym zakresie nie stwierdzono rozbieżności broszury i XSD. Wcześniejsza macierz projektowa nie była traktowana jako dowód implementacji.

| Ścieżka XML | Krotność / ograniczenia | Model i API |
|---|---|---|
| `Faktura/Fa/Platnosc/RachunekBankowy` | 0–100, `TRachunekBankowy` | `Details.BankAccounts` |
| `Faktura/Fa/Platnosc/RachunekBankowyFaktora` | 0–20, ten sam typ | `Details.FactorBankAccounts` |
| `…/NrRB` | Wymagany w każdym podanym rachunku; `TNrRB`, `xsd:string`, 10–34 znaki, bez wzorca cyfr i bez sumy kontrolnej | `InvoiceBankAccount.Number` / `BankAccountDto.Number` |
| `…/SWIFT` | 0–1; `SWIFT_Type`, wzorzec `[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3}){0,1}` | `Swift` |
| `…/RachunekWlasnyBanku` | 0–1; `TRachunekWlasnyBanku`, liczba całkowita 1, 2 lub 3 | nullable `BankOwnAccountType`; DTO: `int?` |
| `…/NazwaBanku` | 0–1; `TZnakowy`, `xsd:token`, 1–256 znaków po normalizacji białych znaków XML | `BankName` |
| `…/OpisRachunku` | 0–1; ten sam typ i zakres | `Description` |

Wiersze `…/` odnoszą się do obu rodzajów rachunków. Typ jest właściwością każdego rachunku osobno. Kolekcje występują w sekwencji, a nie w alternatywie `choice`: można podać obie, jedną albo żadną. Nie dodano zależności od formy płatności ani wymogu rachunku sprzedawcy przy podaniu rachunku faktora.

`RachunekWlasnyBanku` opisuje rachunki własne banków/SKOK, a nie ogólne kategorie rachunków klienta:

- `1` / `PurchasedReceivables`: rozliczanie wierzytelności nabytych przez bank/SKOK.
- `2` / `CollectionsForSupplier`: pobieranie należności i przekazywanie ich dostawcy/usługodawcy.
- `3` / `OwnOperationsNonSettlement`: gospodarka własna banku/SKOK, rachunek niebędący rozliczeniowym.
- `null`: oznaczenia nie podano. Wartość `0` nie jest dopuszczalną kategorią.

## Walidacja

`InvoiceBankAccountValidator` jest wywoływany przez istniejący `SalesInvoiceValidator`. Korzystają z niego zarówno zapis projektu, jak i pełna walidacja przed zatwierdzeniem. Błędy mają stabilne kody oraz ścieżki indeksowane, np. `details.factorBankAccounts[0].bankOwnAccountType`.

Nie dodano kontroli IBAN/NRB, cyfr ani sumy kontrolnej — XSD ich nie definiuje. Sprawdzane są liczebność kolekcji, niepuste wpisy, wymagany numer, dozwolone oznaczenie, SWIFT, długości tekstów i znaki XML. Puste pola opcjonalne należy pominąć przez `null`; pusty tekst nie spełnia ich typu XSD. Oryginalne wartości i kolejność pozostają zachowane; normalizacja `xsd:token` służy wyłącznie sprawdzeniu ograniczeń.

Testy porównują przypadki z rzeczywistym `TRachunekBankowy` z lokalnych XSD, bez pobierania importów z sieci. Uwzględniono zachowanie walidatora XSD platformy .NET, który dla wzorca SWIFT dopuszcza również końcowy LF po poprawnym kodzie; walidacja aplikacji nie zaostrza tego zachowania i nie usuwa znaku z danych użytkownika. Docelowy generator powinien zachować tę samą semantykę walidacji lub jawnie rozstrzygnąć ewentualną przyszłą normalizację.

## Zapis, API i snapshot

Nie powstał równoległy typ rachunku faktora. Obie kolekcje używają `InvoiceBankAccount`, `BankAccountDto` i niezależnego od API `ApprovedBankAccount`.

Pełna ścieżka: DTO → `SalesInvoiceContractMapper` → `SalesInvoiceDraft.Details` → agregat → `SalesInvoiceDetails.Data` → odczyt DTO oraz `SalesInvoiceSnapshotCapture.Input` → zatwierdzony snapshot `Input.Details` i istniejąca projekcja `Disclosure/Details`.

Istniejąca kolumna JSON `SalesInvoiceDetails.Data` przechowuje rozszerzone dane. Konfiguracja EF Core i schemat SQL nie zmieniają się; migracja nie jest potrzebna. Stary JSON danych projektu bez kolekcji faktora jest odczytywany jako pusty zbiór; stary rachunek ma opcjonalne oznaczenie `null`.

W modelu zatwierdzonego dokumentu nowa kolekcja jest nullable. Historyczny snapshot bez `FactorBankAccounts` zachowuje `null`, a brak `BankOwnAccountType` zachowuje `null`, bez dopisywania pól do historycznego JSON. Nowe zatwierdzenia utrwalają obie kolekcje i rzeczywiście podane oznaczenia. Listy odczytane ze snapshotu są niemodyfikowalne. Nie dodano wersji formatu ani SHA-256.

Porównanie zatwierdzonego dokumentu z agregatem toleruje brak historycznej kolekcji faktora wyłącznie wtedy, gdy agregat nie ma żadnego rachunku faktora. Nie ukrywa pojawienia się ani zmiany rachunku. To zachowuje możliwość numeracji wcześniejszych dokumentów bez zmiany algorytmu numerowania i bez modyfikacji ich snapshotów.

Zasady statusów, `Revision`, `RowVersion`, uprawnień i izolacji tenantów korzystają z istniejących mechanizmów. Zmiana rachunków w projekcie podlega obecnym zasadom ponownego potwierdzenia kwalifikacji. Nie zmieniono procesu cofania zatwierdzenia.

## Weryfikacja i zakres testów

Testy obejmują zgodność z XSD, wartości enum (w tym brak i niedozwolone zero), SWIFT, granice długości, tekst opcjonalny, oba limity kolekcji, niezależność rachunków, kopie kolekcji, zapis/odczyt JSON, brak zmiany modelu EF, starszy snapshot, niemodyfikowalność snapshotu oraz jego porównanie z agregatem.

Testy HTTP/SQL obejmują utworzenie i odczyt po restarcie hosta, aktualizację z zachowaniem kwot VAT, konflikt starego RowVersion, izolację tenantów, brak uwierzytelnienia, niepoprawne aktualizacje bez zapisu, zatwierdzenie, blokadę późniejszej edycji oraz rezerwację numeru na podstawie rozszerzonego snapshotu. Szersza regresja obejmuje istniejące testy VAT, zatwierdzania, snapshotów, numeracji i uprawnień.

Kompilacja: `dotnet build XKsef.slnx --no-restore -v minimal -m:1 -p:UseSharedCompilation=false -nodeReuse:false`.

Regresja: `dotnet test xKsef.Tests/xKsef.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~SalesInvoice|FullyQualifiedName~InvoiceNumber|FullyQualifiedName~FourVat|FullyQualifiedName~ApprovalSnapshot|FullyQualifiedName~RoundedGroupBaseAndAllocatedAmounts|FullyQualifiedName~TenantPermissionServiceTests" -v minimal`.

Bazy testowe są izolowanymi instancjami danych `xKsef_Tests_<GUID>`. Nie migrowano bazy aplikacji, nie wykonywano żądań KSeF, nie implementowano generatora XML ani pozostałych sekcji FA(3).

Wynik końcowy: **370/370 testów, 0 błędów, 0 pominięć**. Kompilacja całego rozwiązania: **0 błędów**, dwa istniejące ostrzeżenia NU1510 dotyczące `System.Security.Cryptography.Xml`. Kontrola `git diff --check` oraz analogiczna kontrola nowych, nieśledzonych plików: poprawna. Bez commita i push.

## Zmienione pliki

- `XKsef.Domain/SalesInvoices/SalesInvoiceTypes.cs` — oznaczenie rachunku i kolekcja faktora.
- `XKsef.Domain/SalesInvoices/SalesInvoice.cs` — kopiowanie i ochrona obu kolekcji.
- `XKsef.Domain/SalesInvoices/SalesInvoiceValidator.cs` — użycie wspólnych reguł rachunków.
- `XKsef.Domain/SalesInvoices/InvoiceBankAccountValidator.cs` — nowy walidator zgodny z XSD.
- `xKsef.Contracts/SalesInvoices/SalesInvoiceContracts.cs` — rozszerzone DTO.
- `xKesf.Application/SalesInvoices/SalesInvoiceContractMapper.cs` — mapowanie w obie strony.
- `xKesf.Application/SalesInvoices/SalesInvoiceIssuanceValidator.cs` — wykorzystanie współdzielonej walidacji zamiast osobnej pętli znaków XML.
- `xKesf.Application/SalesInvoices/Snapshots/ApprovedSalesInvoiceData.cs` — opcjonalne pola snapshotu.
- `xKesf.Application/SalesInvoices/Snapshots/SalesInvoiceApprovalSnapshotWriter.cs` — porównanie historycznego braku kolekcji z rzeczywistym pustym zbiorem.
- `xKsef.Tests/Domain/SalesInvoiceBankAccountTests.cs` — reguły, kolekcje i rzeczywiste XSD.
- `xKsef.Tests/Infrastructure/SalesInvoiceBankAccountModelTests.cs` — istniejący konwerter JSON i brak potrzeby migracji.
- `xKsef.Tests/Integration/SalesInvoiceBankAccountTests.cs` — API, SQL, bezpieczeństwo i proces zatwierdzenia.
- `xKsef.Tests/Application/SalesInvoiceSnapshotTests.cs` — snapshot i historyczny JSON.
- `docs/analysis/FA3_BANK_ACCOUNTS_4B6B.md` — niniejszy raport.

Brak nierozstrzygniętych decyzji biznesowych blokujących zakres 4B.6B. Ewentualne przyszłe wymagania sprawdzania poprawności bankowej numerów lub normalizacji SWIFT stanowiłyby osobne reguły aplikacji i nie zostały przyjęte domyślnie.
