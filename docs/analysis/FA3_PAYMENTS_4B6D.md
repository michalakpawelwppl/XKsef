# 4B.6D — dane płatności zwykłej faktury VAT FA(3)

## Zakres i źródła

Punkt odniesienia: `master`, commit `3102d024fd8d16d6609897f45a03a1a42ba0151c`. Przed implementacją sprawdzono faktyczny model, DTO, mapper, walidatory, konwerter JSON i snapshot oraz [lokalne XSD](fa3-sources/schemat.xsd), linie 3281–3442, ich schematy zależne i [broszurę MF](fa3-sources/broszura-fa3.pdf), tabele 52–58, drukowane strony 109–119. Broszurę porównano z oficjalnym wydaniem: https://ksef.podatki.gov.pl/media/nuclnu0w/broszura-informacyjna-dotyczaca-struktury-logicznej-fa-3.pdf.

Zaimplementowano reprezentację danych płatności, zapis, odczyt, API, walidację struktury i udokumentowanych zależności oraz snapshot. **Przy zamknięciu etapu użytkownik zaakceptował wariant A: płatności mają charakter informacyjny, bez dodatkowego algorytmu salda, automatycznego wyliczania pozostałej należności i blokowania nadpłat poza ograniczeniami FA(3). Sekcja Rozliczenie pozostaje bez zmian.** Nie są wymagane dalsze zmiany funkcjonalne w tym etapie.

## Pełna mapa sekcji Platnosc

Wiersz `Platnosc` opisuje korzeń `/Faktura/Fa/Platnosc`; pozostałe ścieżki są względne wobec tego korzenia. Właściwości odnoszą się do `SalesInvoiceDraft.Details` / `SalesInvoiceDetailsData`; analogiczne właściwości istnieją w DTO i zatwierdzonym `Input.Details`. „Stan przed” oznacza rzeczywistą implementację przed tym etapem, nie propozycje z wcześniejszych raportów.

| Element XML | Warunek/krotność i typ | Odpowiednik aplikacji | Stan przed → po |
|---|---|---|---|
| `Platnosc` | Sekcja opcjonalna 0–1; żadna grupa nie musi być podana | Zestaw opcjonalnych pól w istniejącym `Details`; brak pól płatności nie deklaruje otrzymania zapłaty | CZĘŚCIOWE → GOTOWE w zakresie danych |
| `Zaplacono` | 1 w pierwszej gałęzi opcjonalnego `choice`; `etd:TWybor1`, wyłącznie 1 | `PaymentStatus=PaidOnce (1)` | BRAK → GOTOWE |
| `DataZaplaty` | 1 w tej samej gałęzi, `TData` | `PaymentDate` | BRAK → GOTOWE |
| `ZnacznikZaplatyCzesciowej` | Druga gałąź `choice`; `etd:TWybor1_2`, 1 albo 2 | `PartiallyPaid (2)` → XML 1; `FullyPaidInParts (3)` → XML 2 | BRAK → GOTOWE |
| `ZaplataCzesciowa` | 1–100 w drugiej gałęzi | `PartialPayments`, uporządkowana kolekcja `InvoicePartialPayment` | BRAK → GOTOWE |
| `ZaplataCzesciowa/KwotaZaplatyCzesciowej` | 1, `TKwotowy` | `PartialPayments[i].Amount` | BRAK → GOTOWE |
| `ZaplataCzesciowa/DataZaplatyCzesciowej` | 1, `TData` | `PartialPayments[i].PaymentDate` | BRAK → GOTOWE |
| `ZaplataCzesciowa/FormaPlatnosci` | Opcjonalna gałąź `choice`, `TFormaPlatnosci`: 1–7 | `PartialPayments[i].PaymentMethod` 1–7 | BRAK → GOTOWE |
| `ZaplataCzesciowa/PlatnoscInna` | Alternatywa formy kodowej; wyłącznie 1 | Aplikacyjny `PaymentMethod=8` | BRAK → GOTOWE |
| `ZaplataCzesciowa/OpisPlatnosci` | Wymagany przy `PlatnoscInna`, `TZnakowy` | `PartialPayments[i].OtherPaymentDescription` | BRAK → GOTOWE |
| `TerminPlatnosci` | 0–100; wewnętrzna sekwencja opcjonalna | Istniejące `PaymentTerms` | CZĘŚCIOWE → GOTOWE |
| `TerminPlatnosci/Termin` | 0–1, `TData` | Istniejące `DueDate` | CZĘŚCIOWE → GOTOWE |
| `TerminPlatnosci/TerminOpis` | 0–1; jeśli podany, wszystkie trzy dzieci wymagane | Istniejące `Quantity`, `Unit`, `StartingEvent`; obecność przynajmniej jednego oznacza rozpoczęty opis | CZĘŚCIOWE → GOTOWE |
| `TerminPlatnosci/TerminOpis/Ilosc` | 1, nieograniczony zakresowo `xsd:integer` | `Quantity`, rozszerzone z `int?` do `BigInteger?` | CZĘŚCIOWE → GOTOWE |
| `TerminPlatnosci/TerminOpis/Jednostka` | 1, `TZnakowy50` | Istniejące `Unit` | CZĘŚCIOWE → GOTOWE |
| `TerminPlatnosci/TerminOpis/ZdarzeniePoczatkowe` | 1, `TZnakowy` | Istniejące `StartingEvent` | CZĘŚCIOWE → GOTOWE |
| `FormaPlatnosci` | Opcjonalna gałąź `choice`, `TFormaPlatnosci`: 1–7 | Istniejące `PaymentMethod` 1–7 | CZĘŚCIOWE → GOTOWE |
| `PlatnoscInna` | Alternatywa formy kodowej; `etd:TWybor1`, wyłącznie 1 | Istniejący aplikacyjny `PaymentMethod=8` | CZĘŚCIOWE → GOTOWE |
| `OpisPlatnosci` | Wymagany przy `PlatnoscInna`, `TZnakowy` | Istniejące `OtherPaymentDescription` | CZĘŚCIOWE → GOTOWE |
| `RachunekBankowy` | 0–100, `TRachunekBankowy` | Istniejące `BankAccounts` | GOTOWE → bez przebudowy |
| `RachunekBankowy/NrRB` | 1, `TNrRB`, 10–34 znaki | `BankAccounts[i].Number` | GOTOWE → bez zmian |
| `RachunekBankowy/SWIFT` | 0–1, `SWIFT_Type` | `BankAccounts[i].Swift` | GOTOWE → bez zmian |
| `RachunekBankowy/RachunekWlasnyBanku` | 0–1, 1/2/3 | `BankAccounts[i].BankOwnAccountType` | GOTOWE → bez zmian |
| `RachunekBankowy/NazwaBanku` | 0–1, `TZnakowy` | `BankAccounts[i].BankName` | GOTOWE → bez zmian |
| `RachunekBankowy/OpisRachunku` | 0–1, `TZnakowy` | `BankAccounts[i].Description` | GOTOWE → bez zmian |
| `RachunekBankowyFaktora` | 0–20, `TRachunekBankowy` | Istniejące `FactorBankAccounts` | GOTOWE → bez przebudowy |
| `RachunekBankowyFaktora/NrRB` | 1, `TNrRB`, 10–34 znaki | `FactorBankAccounts[i].Number` | GOTOWE → bez zmian |
| `RachunekBankowyFaktora/SWIFT` | 0–1, `SWIFT_Type` | `FactorBankAccounts[i].Swift` | GOTOWE → bez zmian |
| `RachunekBankowyFaktora/RachunekWlasnyBanku` | 0–1, 1/2/3 | `FactorBankAccounts[i].BankOwnAccountType` | GOTOWE → bez zmian |
| `RachunekBankowyFaktora/NazwaBanku` | 0–1, `TZnakowy` | `FactorBankAccounts[i].BankName` | GOTOWE → bez zmian |
| `RachunekBankowyFaktora/OpisRachunku` | 0–1, `TZnakowy` | `FactorBankAccounts[i].Description` | GOTOWE → bez zmian |
| `Skonto` | 0–1; jeśli podane, oba dzieci wymagane | `CashDiscount`, `InvoiceCashDiscount` | BRAK → GOTOWE |
| `Skonto/WarunkiSkonta` | 1, `TZnakowy` | `CashDiscount.Conditions` | BRAK → GOTOWE |
| `Skonto/WysokoscSkonta` | 1, **tekst** `TZnakowy`, nie pole procentowe | `CashDiscount.AmountDescription` | BRAK → GOTOWE |
| `LinkDoPlatnosci` | 0–1, ograniczenie `TZnakowy512` i specjalny wzorzec | `PaymentLink` | BRAK → GOTOWE |
| `IPKSeF` | 0–1, ograniczenie `TZnakowy20`, maxLength 13 i wzorzec | `KsefPaymentId` | BRAK → GOTOWE |

Statusy GOTOWE dotyczą reprezentacji, API, utrwalania i walidacji wskazanych reguł. Nie oznaczają istnienia generatora XML, operatora płatności ani algorytmu finansowego uzgadniania salda.

## Ograniczenia i znaczenie typów

- `TData`: od 2016-07-01 do 2050-01-01, włącznie.
- `TKwotowy`: `decimal`, do 16 cyfr całkowitych i 2 po przecinku, zakres od −9999999999999999,99 do +9999999999999999,99. Nie zastosowano `float` ani `double`; wartości o większej dokładności, np. 0,001, są odrzucane zamiast zaokrąglane.
- `TZnakowy`, `TZnakowy50`, `TZnakowy512`: `xsd:token`, długości odpowiednio 1–256, 1–50 i 1–512 po normalizacji białych znaków XML. Oryginalne teksty nie są nadpisywane znormalizowaną postacią.
- `TFormaPlatnosci`: 1 gotówka, 2 karta, 3 bon, 4 czek, 5 kredyt, 6 przelew, 7 płatność mobilna. Kod aplikacyjny 8 zachowuje dotychczasowe znaczenie „inna”; przyszłe mapowanie ma wyemitować `PlatnoscInna=1` i opis. Nie wolno emitować `FormaPlatnosci=8`.
- `Ilosc` nie ma w XSD ograniczenia do `Int32` ani dodatniości. Zastąpiono ograniczoną reprezentację nullable `BigInteger`, bez automatycznego obliczania daty. JSON przyjmuje dokładne tokeny całkowite i ciągi całkowite; zapisuje je jako ciągi, by nie utracić precyzji w klientach JavaScript. Wcześniejsze liczby JSON pozostają czytelne.
- `IPKSeF`: dokładnie trzy cyfry i dziesięć znaków ASCII alfanumerycznych, np. `001ABC123DEF4`. To identyfikator otrzymany od agenta rozliczeniowego; aplikacja go nie generuje ani nie utożsamia z numerem faktury lub numerem KSeF.

Wzorzec linku z XSD (po odkodowaniu XML `&amp;`):

```text
(https?)://([a-zA-Z0-9][a-zA-Z0-9-]*\.)+[a-zA-Z]{2,}(:[0-9]{1,5})?(/[^\s?#]*)?\?([^#\s]*&)?IPKSeF=[0-9]{3}[a-zA-Z0-9]{10}(&[^#\s]*)?(#.*)?
```

Nie zastąpiono go ogólną kontrolą URI. W szczególności sam poprawny URL bez parametru `IPKSeF` nie spełnia schematu. Nie wymagano HTTPS zamiast dopuszczonego przez XSD HTTP. Link i osobne `IPKSeF` są niezależnie opcjonalne — aplikacja nie dopisuje brakującego identyfikatora na podstawie linku i nie wykonuje żądań HTTP pod ten adres.

## Walidacja strukturalna, kompletność i reguły dokumentacji

Wspólny `SalesInvoicePaymentValidator` zastępuje rozproszone reguły form i terminów. Jest używany podczas walidacji Draft oraz z `forIssuance=true` podczas pełnej walidacji. Zachowano kontrolę poprawności znanych wartości i struktur podczas zapisu. Niekompletny Draft może zawierać rozpoczęte dane zapłaty, opis innej formy albo skonto; kompletność jest wymagana przed zatwierdzeniem. Wymóg opisu dla aplikacyjnej formy 8 został przeniesiony do pełnej walidacji, bez zmiany znaczenia tego kodu.

Wynik walidacji korzysta z istniejących list błędów, polskich komunikatów i stabilnych kodów, np. `PaymentChoiceConflict`, `PaymentDateRequired`, `PartialPaymentAmountRequired`, `PaymentTermDescriptionIncomplete`, `PaymentLinkInvalid`. Ścieżki wskazują również indeks wpłaty.

Reguły wynikające ze struktury XSD:

- Zapłata jednorazowa i zapłaty w częściach są alternatywami. Jednorazowa wymaga daty; wpłaty wymagają odpowiedniego markera, dat i kwot.
- W wybranej gałęzi zapłat częściowych jest 1–100 wpisów; forma każdego wpisu może być pominięta.
- Podanie innej formy wymaga opisu; opis z kodem zwykłej formy albo bez wybranej innej formy nie ma poprawnego odwzorowania w `choice`.
- Opis terminu wymaga ilości, jednostki i zdarzenia. Termin kalendarzowy i opis mogą wystąpić razem; pusty element `TerminPlatnosci` jest strukturalnie dopuszczalny.
- Skonto wymaga obu tekstów. Nie dodano stawki procentowej, progu ani algorytmu skonta.

Reguły dokumentacji i aplikacji, wyraźnie odrębne od XSD:

- Pełna zapłata w częściach oznacza co najmniej dwie wpłaty. Sam XSD dopuszcza pojedynczy wpis także przy XML markerze 2; walidacja znaczenia z broszury wymaga dwóch.
- Wykazane otrzymane wpłaty muszą mieć daty niepóźniejsze niż planowana data wystawienia. Nie nałożono takiego ograniczenia na terminy płatności: mogą dotyczyć przyszłości lub przeszłości.
- Jeśli podano link i osobne `IPKSeF`, oba muszą identyfikować tę samą płatność. To kontrola spójności danych aplikacji, a nie zależność formalnie wymuszona przez XSD.

Broszura ma redakcyjną niespójność: opis pól tabeli 53 odnosi się do markera 1, podczas gdy definicja markera i przykład 26 jednoznacznie dopuszczają listę wpłat także dla markera 2. Nie pominięto drugiego wariantu. XSD i całościowy opis MF pozwalają ustalić strukturę bez zgadywania. Nie potraktowano samej krotności XSD jako dowodu spełnienia wszystkich warunków biznesowych.

## Relacja do kwot — zaakceptowany wariant A

Nowe wpłaty są informacją o płatnościach, a nie odliczeniami sekcji `Rozliczenie`. Istniejący wynik `GrossAmount + ChargesTotal − DeductionsTotal` pozostaje niezmieniony. Nie zmieniają się podstawy VAT, VAT w PLN, rabaty ani kwoty pozycji. Nie wylicza się nowej kwoty „pozostało do zapłaty” ani automatycznej nadpłaty na podstawie listy wpłat. Nie blokuje się kwot wpłat przez porównanie do arbitralnie wybranej należności.

Podczas analizy przedstawiono dwa warianty. Użytkownik zaakceptował pierwszy (A); drugi nie należy do zakresu tego etapu:

1. **Wariant informacyjny, zaakceptowany:** utrwalać wpłaty i deklarację użytkownika, bez nowego salda oraz bez finansowego blokowania nadpłat. Obecna implementacja dostarcza niezależną reprezentację i kontrolę struktury. Dodatkowy algorytm salda nie jest częścią etapu 4B.6D.
2. **Osobne saldo płatności:** najpierw uzgodnić bazę (brutto faktury czy wynik Rozliczenia), sposób uwzględnienia ewentualnych kwot już rozliczonych w odliczeniach, reprezentację zapłaty jednorazowej bez osobnego pola kwoty w FA(3) i obsługę nadpłaty/zwrotu. Dopiero później dodać jawnie uzgodniony algorytm i finansowe reguły zgodności markera.

Przykład z analizy: faktura 123, obciążenia 10, odliczenia 3 i wpłata 30. Pozostała kwota wyniosłaby 93 przy bazie brutto lub 100 przy bazie Rozliczenia, a odliczenie mogłoby wymagać dodatkowej kwalifikacji, by uniknąć podwójnego uwzględnienia tej samej operacji. Zgodnie z zaakceptowanym wariantem A żaden z tych wzorów nie jest wdrażany.

Walidacja kwotowej zgodności deklaracji „w części/w całości” z sumą wpłat oraz interpretacja kwot zerowych/ujemnych jako operacji finansowych nie stanowią zakresu 4B.6D zgodnie z wariantem A. Typ XSD dopuszcza signed `TKwotowy`; strukturalne przyjęcie kwoty nie oznacza zrealizowania zwrotu ani nadpłaty. Globalna unikalność i autentyczność `IPKSeF` wymaga udziału zewnętrznego agenta — bez takiej integracji sprawdzany jest format i spójność z podanym linkiem, nie globalna rejestracja identyfikatora.

## API, zapis i snapshot

Dodano w istniejącym `SalesInvoiceDetailsData` i `SalesInvoiceDetailsDto`: `PaymentStatus`, `PaymentDate`, `PartialPayments`, `CashDiscount`, `PaymentLink` i `KsefPaymentId`. Nie utworzono alternatywnego modelu rachunków ani drugiej reprezentacji form/terminów. Kolekcja używa `InvoicePartialPayment` / `PartialPaymentDto`, skonto `InvoiceCashDiscount` / `CashDiscountDto`. Kwoty API korzystają z istniejącego `ExactDecimal`.

Ścieżka danych: DTO → `SalesInvoiceContractMapper` → `SalesInvoiceDraft.Details` → agregat z kopiami list → `SalesInvoiceDetails.Data` → odczyt DTO i zatwierdzony `Input.Details`. Stosowane są dotychczasowe endpointy, TenantId, Revision, RowVersion oraz uprawnienia. Edycja zatwierdzonych dokumentów, cofanie zatwierdzenia i blokady związane z numeracją nie zostały zmienione.

Snapshot ma własne `ApprovedPartialPayment` i `ApprovedCashDiscount`, niezależne od DTO API. Nowe pola są nullable, a historyczny brak kolekcji wpłat pozostaje `null`. Nie wprowadzono automatycznych wartości `false` ani `0` dla informacji, których wcześniej nie zapisywano. Brak deklaracji w FA(3) nie dowodzi, że faktura jest nieopłacona; dlatego model nie dodaje sztucznej wartości `Unpaid=0`.

Odczyt starego snapshotu nie przelicza VAT, Rozliczenia ani płatności i nie zmienia JSON lub audytu. Porównanie z agregatem toleruje historyczny brak `PartialPayments` tylko przy rzeczywiście pustej kolekcji agregatu; dodanie wpłat nadal powoduje niezgodność. Historyczne całkowite tokeny `Quantity` są odczytywane dokładnie, bez konwersji zmiennoprzecinkowej. Primitive converter nie wywołuje mapperów API ani kalkulatorów. Snapshot przechowuje kolejność, kwoty, daty i oryginalne teksty, a jego listy pozostają niemodyfikowalne. Istniejąca projekcja `Disclosure/Details` obejmuje nowe pola automatycznie.

## Baza i ograniczenia etapu

Dotychczasowy konwerter JSON `SalesInvoiceDetails.Data` przechowuje wszystkie nowe dane. Model relacyjny EF nie zmienił się: **migracja nie jest potrzebna i nie została utworzona**. Kontrola `has-pending-model-changes` potwierdziła brak zmian modelu. Nie zastosowano migracji do bazy aplikacji i nie usuwano jej danych.

Nie zmieniono React, kalkulatora VAT, algorytmu numerowania, rachunków z 4B.6B ani modelu/kalkulatora Rozliczenia z 4B.6C. Nie dodano wersjonowania snapshotów, SHA-256, generatora XML, żądań KSeF, agenta płatniczego ani wykonywania płatności. Bez commita i push.

## Weryfikacja

Kompilacja całego rozwiązania: **0 błędów**, dwa istniejące ostrzeżenia NU1510. Regresja funkcjonalna wykonana podczas implementacji: **429/429 testów**, 0 błędów i 0 pominięć. Były to odpowiednie testy jednostkowe i integracyjne, a nie każdy projekt testowy rozwiązania. Wynik późniejszej pełnej weryfikacji zamknięcia podano poniżej.

Testy sprawdzają brak deklaracji płatności, zapłatę jednorazową, jedną i wiele wpłat, krotności 1/100/101, wariant pełnej zapłaty w częściach, formy 1–8, konflikty gałęzi, kwoty i daty, skonto, opisowe terminy i duże ilości całkowite, link 512/513 znaków, format i spójność IPKSeF, zapis/odczyt API i SQL, aktualizację, blokadę zatwierdzonej edycji, Revision/RowVersion, izolację tenantów, uwierzytelnienie, snapshot i starszy JSON. Regresja obejmuje także rozliczenia, VAT, rachunki, uprawnienia i numerację. Testy XSD korzystają z rzeczywistego fragmentu `Platnosc` i czterech lokalnych schematów, z wyłączonym pobieraniem importów z sieci.

Polecenia:

```text
dotnet build XKsef.slnx --no-restore -v minimal -m:1 -p:UseSharedCompilation=false -nodeReuse:false
dotnet test xKsef.Tests/xKsef.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~SalesInvoice|FullyQualifiedName~InvoiceNumber|FullyQualifiedName~FourVat|FullyQualifiedName~ApprovalSnapshot|FullyQualifiedName~RoundedGroupBaseAndAllocatedAmounts|FullyQualifiedName~TenantPermissionServiceTests" -v minimal
dotnet ef migrations has-pending-model-changes --project xKsef.Infrastructure/xKsef.Infrastructure.csproj --startup-project xKsef.Infrastructure/xKsef.Infrastructure.csproj --context AppDbContext --no-build
```

Testy integracyjne korzystają z dotychczasowych izolowanych baz `xKsef_Tests_<GUID>`. Nie uruchamiano głównego hosta API ani połączeń do KSeF.

## Pierwsza pełna weryfikacja zamknięcia — wynik historyczny

Po zaakceptowaniu wariantu A ponownie skompilowano rozwiązanie i wykonano pełny domyślny zestaw: `dotnet test XKsef.slnx --no-build --no-restore -v minimal`, z wyłączonymi ręcznymi testami sieciowymi GUS. Kompilacja zakończyła się poprawnie, ale pełny zestaw zakończył się kodem 1:

| Projekt | Poprawne | Błędy | Pominięte | Łącznie |
|---|---:|---:|---:|---:|
| `xKsef.Tests` | 1798 | 104 | 13 | 1915 |
| `xKsef.GusBir.ManualTests` | 13 | 0 | 2 | 15 |
| Razem | **1811** | **104** | **15** | **1930** |

Zaobserwowane przyczyny obejmują:

- `System.FormatException` podczas wykonywania wygenerowanego skryptu przez `ExecuteSqlRawAsync(schema)` w `xKsef.Tests/Infrastructure/ContractorRepositoryTests.cs:221` i `xKsef.Tests/Infrastructure/RegistrationTests.cs:280`. Ślad wskazuje etap interpretowania formatu SQL w `RawSqlCommandBuilder`, przed sprawdzeniem zachowania funkcjonalnego testu.
- Nieaktualną sztywną listę migracji w `xKsef.Tests/Integration/RegistrationSqlServerTests.cs:176`: test oczekuje listy kończącej się `AddSalesInvoiceDrafts`, mimo obecności dalszych migracji zatwierdzania, numeracji i FA(3).

Te pliki nie były modyfikowane w 4B.6D ani podczas pierwszej próby zamknięcia. Nie naprawiano ich wtedy po niepowodzeniu testów, zgodnie z poleceniem zatrzymania przed commitem. Wynik ten stał się podstawą osobnego zadania diagnostyki infrastruktury testowej opisanego poniżej. Nie wykonano commita, push ani migracji bazy aplikacji.

## Diagnostyka i naprawa infrastruktury testowej

Zebrano pełne wyniki TRX przed naprawą i przeanalizowano wszystkie 104 niepowodzenia. Nie ograniczono diagnozy do komunikatów widocznych w skróconej konsoli.

| Wspólna przyczyna | Liczba | Rozbicie |
|---|---:|---|
| Wygenerowany SQL z klamrami JSON przekazany do formatowania złożonego EF | **96** | 74 przez `ContractorTestStore` (62 przypadki `ContractorApiTests`, 12 `ContractorRepositoryTests`), 22 przez SQLite `RegistrationApi` |
| Nieaktualne oczekiwania migracji i bieżącego modelu | **8** | 6 przypadków `RegistrationSqlServerTests`, 1 przypadek kolejności/liczby migracji i tabel, 1 przypadek porównania modelu do niewłaściwej historycznej migracji |
| Razem | **104** | Brak niesklasyfikowanych niepowodzeń |

### Źródło FormatException i różnice dostawców SQL

`GenerateCreateScript()` zawierał m.in. domyślne literały `N'{"Version":1}'` dla `SalesInvoices.Fa3` i `N'{}'` dla `SalesInvoiceLines.Annex15`. `ExecuteSqlRawAsync(schema)` przekazywało ten skrypt przez `RawSqlCommandBuilder` i `String.Format`. Pierwszy znak `{` w JSON został potraktowany jak początek zastępnika `{0}`. Zamiast cyfry indeksu wystąpił cudzysłów otwierający `"Version"`, co spowodowało `FormatException` przy pozycji 15086. Nie był to błąd parsowania connection stringa ani danych płatności. Wyjątek występował przed wykonaniem DDL i przed asercjami funkcjonalnymi.

Nowy, wyłącznie testowy `SqliteTestSchema` wykonuje wygenerowane DDL bezpośrednio przez `SqliteCommand`, bez formatowania klamer. Zachowuje wszystkie tabele, indeksy, CHECK-i i FK. Ograniczona adaptacja składni dotyczy `varbinary(max)` → `BLOB` oraz prefiksu literału Unicode `N'…'` w klauzulach `DEFAULT`; SQLite nie potrzebuje prefiksu `N`. Funkcje `LEN`, `DATALENGTH`, kolacja oraz testowy odpowiednik `ISJSON` są rejestrowane na połączeniu SQLite. Odpowiednik `ISJSON` rozróżnia obiekty/tablice, skalary, błędny JSON i SQL NULL; nie zwraca bezwarunkowo poprawności.

Nowe testy potwierdzają zachowanie obu rzeczywistych domyślnych JSON, pełnej listy tabel i wszystkich definicji CHECK, aktywność FK oraz fizyczne odrzucanie błędnego JSON przez CHECK. Helper przyjmuje wyłącznie własne, otwarte połączenie `Data Source=:memory:`. Testy potwierdzają odmowę użycia innego połączenia i bazy plikowej, bez otwierania lub tworzenia pliku.

### Nieaktualne oczekiwania migracji/modelu

Test rejestracji SQL Server oczekiwał siedmiu migracji kończących się na `AddSalesInvoiceDrafts`. Projekt i baza testowa mają 15 migracji, do `20261128162009_AddSalesInvoiceSettlement`. Integracyjna asercja porównuje teraz pełną uporządkowaną listę migracji skompilowanych w projekcie z całą historią ich zastosowania — nadal wykrywa migracje brakujące i nadmiarowe. Usunięto duplikację ręcznej listy w fixture.

Kanoniczny test modelu nadal jawnie kontroluje pełną listę i kolejność wszystkich 15 identyfikatorów. Zamiast samego nieaktualnego licznika 26 tabel sprawdza dokładny zestaw 33 nazw tabel wynikających z obecnych migracji. Porównanie bieżącego modelu i snapshotu pozostało bezwarunkowe; target jest wybierany z ostatniej migracji zestawu, zamiast z historycznej `AddSalesInvoiceDrafts`.

Po skorygowaniu licznika ujawniło się dodatkowe stare założenie tego samego testu: dopuszczał tylko tworzenie tabel/indeksów/schematów. Obecny model numeracji ma cykliczne relacje, dla których EF emituje dwa `AddForeignKeyOperation` po utworzeniu tabel. Test nie ignoruje tych operacji: porównuje cały komplet wygenerowanych FK z modelem, w tym tabele i schematy, kolumny, klucze nadrzędne oraz akcje usuwania. Sprawdza również, że odroczone FK występują po utworzeniu obu tabel. Nie dopuszczono operacji aktualizowania lub usuwania danych.

### Weryfikacja na czystym HEAD

Commit `3102d024fd8d16d6609897f45a03a1a42ba0151c` odtworzono przez `git archive` w osobnym katalogu tymczasowym, bez stasha, resetu ani nadpisywania bieżących zmian. Wykonano restore, build i pełny przebieg testów z osobnymi wynikami TRX.

| Stan źródeł | Passed | Failed | Skipped | Łącznie |
|---|---:|---:|---:|---:|
| Czysty `3102d02`, bez 4B.6D | 1781 | 104 | 15 | 1900 |
| 4B.6D, przed naprawą fixture'ów | 1811 | 104 | 15 | 1930 |
| 4B.6D i naprawiona infrastruktura | **1927** | **0** | **15** | **1942** |

Porównano multizbiory nazw i komunikatów wszystkich nieudanych przypadków, uwzględniając powtarzające się skrócone nazwy testów parametryzowanych: **0 różnic** między czystym HEAD a stanem 4B.6D. Wszystkie wcześniejsze 104 przypadki mają po naprawie wynik Passed. Wniosek o braku związku tych awarii z nowymi danymi płatności wynika z reprodukcji, nie z założenia.

### Połączenia i ochrona danych

Fixture'y kontrahentów i rejestracji SQLite korzystają z `Data Source=:memory:`; konfiguracyjny adres `offline.invalid` w rejestracji nie jest używany jako rzeczywista baza. Testy SQL Server nadpisują konfigurację przez `UseIsolatedSqlServer` i sprawdzają cel w `SqlServerTestDatabase.VerifyAsync`: `(LocalDb)\MSSQLLocalDB`, wygenerowane dla procesu `xKsef_Tests_<GUID>` i potwierdzone `IsLocalDB=1`. Tworzenie/usuwanie dotyczy wyłącznie baz będących własnością fixture, z kontrolą prefiksu i formatu nazwy. Nie wykonano migracji ani usuwania danych lokalnej bazy aplikacji `xKsef`.

Nie usunięto asercji ani istniejących testów i nie dodano nowych pominięć. Zachowano 15 dotychczasowych pominięć testów ręcznych wymagających osobnego opt-in: 13 limitera GUS/SQL i 2 sieciowe GUS. Dodano 12 testów regresyjnych samej infrastruktury. Te same klucze opt-in były niewłączone przed naprawą.

### Wyniki i zakres zmian naprawczych

- Testy klas obejmujących wszystkie poprzednie niepowodzenia oraz nowe testy helpera: **127/127 Passed**.
- Pełny zestaw `dotnet test XKsef.slnx --no-build --no-restore -v quiet --logger trx`: **1927 Passed, 0 Failed, 15 Skipped**. `xKsef.Tests`: 1914/0/13; `xKsef.GusBir.ManualTests`: 13/0/2.
- Kompilacja całego rozwiązania: **0 błędów**, dwa dotychczasowe ostrzeżenia NU1510.
- `has-pending-model-changes`: brak zmian modelu; brak nowych lub zmodyfikowanych migracji i snapshotu modelu EF.
- Porównanie skrótów plików przed i po zadaniu potwierdziło zachowanie wszystkich 15 istniejących plików kodu/testów etapu 4B.6D bajt w bajt. Uaktualniono jedynie niniejszą dokumentację poza nowymi poprawkami infrastruktury.

Naprawione pliki infrastruktury:

1. `xKsef.Tests/Infrastructure/SqliteTestSchema.cs` — wspólne przygotowanie SQLite in-memory, zachowanie literalnego SQL i ograniczeń.
2. `xKsef.Tests/Infrastructure/SqliteTestSchemaTests.cs` — 12 testów regresyjnych i ochrony celu połączenia.
3. `xKsef.Tests/Infrastructure/ContractorRepositoryTests.cs` — użycie wspólnego helpera.
4. `xKsef.Tests/Infrastructure/RegistrationTests.cs` — użycie wspólnego helpera.
5. `xKsef.Tests/Integration/RegistrationSqlServerTests.cs` — pełna kontrola zastosowania bieżącego zestawu migracji.
6. `xKsef.Tests/Infrastructure/StoredInvoiceModelAndMigrationTests.cs` — aktualny, kompletny katalog migracji/tabel i ścisłe porównanie modelu oraz FK.

Wyniki TRX i pełne logi diagnostyczne zapisano poza repozytorium w katalogu tymczasowym `xksef-test-diagnostics-ff2ddfcf55cf4382a9a46b241aac5d98`, w podkatalogach `before`, `head-confirmed`, `fixed-targeted` i `after`. Nie ma nierozstrzygniętych niepowodzeń testów. Poprawki dotyczą wyłącznie infrastruktury testowej i dokumentacji; nie zmieniono logiki aplikacji, walidacji biznesowej, VAT, numeracji, rozliczeń ani płatności. Bez commita i push.

## Zmienione pliki

- `XKsef.Domain/SalesInvoices/InvoicePayment.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoicePaymentValidator.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoiceTypes.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoice.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoiceValidator.cs`
- `xKsef.Contracts/SalesInvoices/InvoicePaymentContracts.cs`
- `xKsef.Contracts/SalesInvoices/SalesInvoiceContracts.cs`
- `xKesf.Application/SalesInvoices/SalesInvoiceContractMapper.cs`
- `xKesf.Application/SalesInvoices/SalesInvoiceIssuanceValidator.cs`
- `xKesf.Application/SalesInvoices/Snapshots/ApprovedSalesInvoiceData.cs`
- `xKesf.Application/SalesInvoices/Snapshots/SalesInvoiceApprovalSnapshotWriter.cs`
- `xKsef.Tests/Domain/SalesInvoicePaymentTests.cs`
- `xKsef.Tests/Infrastructure/SalesInvoicePaymentModelTests.cs`
- `xKsef.Tests/Integration/SalesInvoicePaymentTests.cs`
- `xKsef.Tests/Application/SalesInvoiceSnapshotTests.cs`
- `docs/analysis/FA3_PAYMENTS_4B6D.md`
