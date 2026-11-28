# 4B.6C — rozliczenia zwykłej faktury VAT

## Źródła i potwierdzone reguły

- Lokalny [schemat FA(3)](fa3-sources/schemat.xsd), wzór `13775`: element `Rozliczenie`, linie 3212–3280, oraz typy `TKwotowy` i `TZnakowy`.
- [Broszura MF](fa3-sources/broszura-fa3.pdf), drukowane strony 104–107, tabele 49–51 i przykład 24. Przy analizie sprawdzono również jej oficjalną publikację: https://ksef.podatki.gov.pl/media/nuclnu0w/broszura-informacyjna-dotyczaca-struktury-logicznej-fa-3.pdf.

Nie stwierdzono rozbieżności XSD i dokumentacji w zakresie struktury i wzoru rozliczenia. XSD określa typy i krotności; znaczenie ekonomiczne wyniku wynika z dokumentacji i jej przykładu, a nie z samego typu liczbowego.

| Element pod `Faktura/Fa/Rozliczenie` | Krotność i typ | Implementacja |
|---|---|---|
| `Rozliczenie` | Sekcja 0–1, wszystkie jej grupy mogą być pominięte | Nullable `SalesInvoiceDetailsData.Settlement` |
| `Obciazenia` | 0–100 | `InvoiceSettlementData.Charges` |
| `Obciazenia/Kwota` | 1–1, `TKwotowy` | `InvoiceSettlementEntry.Amount` |
| `Obciazenia/Powod` | 1–1, `TZnakowy` | `InvoiceSettlementEntry.Reason` |
| `SumaObciazen` | 0–1, `TKwotowy` | Obliczone `ChargesTotal` |
| `Odliczenia` | 0–100 | `InvoiceSettlementData.Deductions` |
| `Odliczenia/Kwota` | 1–1, `TKwotowy` | Wspólny typ pozycji, `Amount` |
| `Odliczenia/Powod` | 1–1, `TZnakowy` | Wspólny typ pozycji, `Reason` |
| `SumaOdliczen` | 0–1, `TKwotowy` | Obliczone `DeductionsTotal` |
| `DoZaplaty` | Jedna z gałęzi opcjonalnego `choice` | `InvoiceSettlementAmounts.AmountDue` |
| `DoRozliczenia` | Druga gałąź tego samego `choice`; nie razem z `DoZaplaty` | `InvoiceSettlementAmounts.Overpayment` |

`TKwotowy` jest oparty na `decimal`: `totalDigits=18`, `fractionDigits=2`, wzorzec `-?([1-9]\d{0,15}|0)(\.\d{1,2})?`. Zakres obsługiwanych kwot wynosi zatem od −9999999999999999,99 do +9999999999999999,99. Typ dopuszcza wartości ujemne i zero; nie dodano nieuzasadnionego ograniczenia wszystkich wpisów do wartości dodatnich.

`TZnakowy` dziedziczy po `xsd:token`, ma długość 1–256 po normalizacji białych znaków XML. Powód jest wymagany dla kompletnego dokumentu. Wpisy nie muszą mieć unikalnych opisów ani identyfikatorów — XSD tego nie wymaga. Zachowywana jest kolejność list.

## Obliczenia i rozdzielenie od VAT

`InvoiceSettlementCalculator` stosuje wzór:

`saldo = GrossAmount + suma(Charges.Amount) − suma(Deductions.Amount)`.

Obecny model zwykłej faktury VAT mapuje `GrossAmount` na `P_15`. Dla salda dodatniego system zapisuje `AmountDue`; dla ujemnego zapisuje dodatnią wartość przeciwną w `Overpayment`. Dla zera wybiera `AmountDue=0` i `Overpayment=null`. Jest to jawna reguła aplikacji rozstrzygająca reprezentację zera w dopuszczalnym wyborze XSD. Brak sekcji daje `SettlementAmounts=null`; pusta, świadomie dodana sekcja daje sumy zero i wynik równy kwocie faktury, gdy jest ona znana.

Przykład MF: `246 + 17 − 300 = −37`, zatem `DoRozliczenia=37`. Przykład należności: `246 + 17 − 10 = 253`, zatem `DoZaplaty=253`.

Wszystkie obliczenia używają `decimal`. Kwoty wejściowe mogą mieć najwyżej dwie istotne cyfry po przecinku, a kwota faktury jest już wynikiem monetarnym kalkulatora VAT. Dodawanie i odejmowanie nie wymaga dalszego zaokrąglania. Kwota `0,005` jest odrzucana zamiast cicho zmieniana. Sumy i końcowy wynik również muszą mieścić się w `TKwotowy`.

Nie zmieniono `SalesInvoiceCalculator`, jego metod, podstaw podatkowych, VAT ani wyników pozycji. Rozliczenie jest prowadzone w walucie faktury, bez automatycznego przeliczania obciążeń na PLN. Nie zmienia VAT w PLN. W modelu płatności są obecnie warunki, forma oraz rachunki, ale nie ma kwot wpłat, które należałoby automatycznie odejmować. Nie powiązano rozliczenia automatycznie z wpłatami, zaliczkami ani zarezerwowanymi typami odrębnych dokumentów.

## Projekt, API i walidacja

Użytkownik edytuje wyłącznie `Draft.Details.Settlement.Charges/Deductions` poprzez istniejące endpointy faktur. DTO wejściowe nie zawiera sum ani wyniku. Nieznane pola wewnątrz DTO rozliczenia są odrzucane, w tym próba podania `amountDue`. Wyniki API zwraca osobno w `SalesInvoiceDto.SettlementAmounts`, z użyciem istniejącego `ExactDecimal`.

Zgodnie z dotychczasową zasadą projektu Draft może być niekompletny: `Amount` i `Reason` mogą nie być jeszcze podane. Znane kwoty muszą spełniać zakres i precyzję; podane powody muszą spełniać maksymalną długość i reguły znaków XML. Niepoprawne kolekcje, przekroczenie 100 wpisów i wpisy `null` są błędami struktury.

Suma kolekcji z nieznaną kwotą pozostaje `null`, nie zero. Wynik jest nieznany, dopóki nie ma kwoty faktury i wszystkich kwot wpisów. Znana, pusta kolekcja ma sumę zero. Pełna walidacja wymaga kwoty i powodu każdego wpisu, kompletności wyniku oraz zgodności utrwalonych sum i wyniku z fakturą. Nie naprawia niezgodnych danych w trakcie odczytu lub walidacji. Dopiero jawna aktualizacja/przeliczenie Draft wylicza nowe wyniki.

Całość podlega istniejącym kontrolom TenantId, Revision i RowVersion. Obliczenie rozliczenia następuje przed zmianą agregatu; błąd zakresu nie pozostawia częściowo zmienionego obiektu. Zapis nagłówka i szczegółów korzysta z istniejącej transakcji EF. Zatwierdzony dokument pozostaje zablokowany do edycji. Nie zmieniono uprawnień, cofania zatwierdzenia ani blokad numerowania.

## Utrwalanie i migracja

Wejście jest zapisane w dotychczasowej kolumnie JSON `SalesInvoiceDetails.Data`, jako opcjonalne `Settlement`. Wyniki są utrwalane osobno jako opcjonalny JSON `SalesInvoices.SettlementAmounts`. Dzięki temu odczyt projektu nie musi ponownie obliczać rozliczenia, a snapshot może oddzielać wejście od wyników.

Migracja `20261128162009_AddSalesInvoiceSettlement` dodaje nullable `nvarchar(max)` i warunek `SettlementAmounts IS NULL OR ISJSON(SettlementAmounts)=1`. Nie wykonuje aktualizacji, przeliczania ani usuwania istniejących wierszy. Zawiera plik Designer i zaktualizowany model snapshotu EF. Przy zamknięciu etapu zastosowano ją do lokalnej bazy deweloperskiej `xKsef` na `(LocalDb)\MSSQLLocalDB`, po potwierdzeniu tożsamości bazy i sprawdzeniu, że była jedyną oczekującą migracją. Potwierdzono obecność kolumny i aktywnego, zaufanego warunku JSON oraz niezmienione liczby wierszy wszystkich istniejących tabel danych. Brak oczekujących migracji i zmian modelu EF Core.

## Snapshot zatwierdzenia

- `Input.Details.Settlement`: faktycznie zatwierdzone listy i powody, niezależne od DTO API.
- `Calculations.SettlementAmounts`: utrwalone wyniki, bez powielania kwoty brutto faktury.
- `Disclosure`: dane wejściowe w istniejącym `Details` oraz obliczone `SettlementAmounts`.

Nie dodano wersji formatu ani SHA-256. Historyczny JSON bez nowej sekcji i wyników odczytuje się jako `null`, bez migracji treści i bez przeliczania historycznych wartości. Takie dokumenty nadal przechodzą porównanie snapshotu z agregatem. Nowy snapshot z sekcją rozliczenia musi mieć kompletne, spójne wyniki; czytnik sprawdza ich utrwalone tożsamości arytmetyczne bez uruchamiania kalkulatora. Listy są niemodyfikowalne. Zmiana danych bieżącego obiektu nie zmienia zatwierdzonego JSON.

## Weryfikacja

Testy obejmują przykład MF, kwotę do zapłaty, nadpłatę, zero, signed `TKwotowy`, obie waluty PLN/EUR, dokładność groszową, brak sekcji i pustą sekcję, niekompletny projekt, limity, XML, zakres sum, kopiowanie list, kontrolę faktycznego fragmentu XSD, zapis i odczyt SQL, aktualizację i przeliczenie, snapshot, stary JSON, wykrywanie niezgodnych wyników, autoryzację, tenant isolation, konflikty wersji i numerację.

Test migracji sprawdza zachowanie istniejących bajtów JSON i danych audytowych zatwierdzenia oraz brak automatycznego wypełniania wyników. Istniejący test migracji uproszczenia snapshotów zachowuje swoje asercje; dane poprzedniego schematu są przygotowywane SQL, aby nie zależały od nowych kolumn modelu EF. Test zestawu migracji nadal sprawdza obecność uproszczenia snapshotów, a jako ostatnią oczekuje migracji rozliczenia.

Kompilacja: `dotnet build XKsef.slnx --no-restore -v minimal -m:1 -p:UseSharedCompilation=false -nodeReuse:false`.

Regresja: `dotnet test xKsef.Tests/xKsef.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~SalesInvoice|FullyQualifiedName~InvoiceNumber|FullyQualifiedName~FourVat|FullyQualifiedName~ApprovalSnapshot|FullyQualifiedName~RoundedGroupBaseAndAllocatedAmounts|FullyQualifiedName~TenantPermissionServiceTests" -v minimal`.

Testy SQL korzystają wyłącznie z baz `xKsef_Tests_<GUID>` tworzonych dla testów. Nie uruchamiano głównego hosta aplikacji ani komunikacji KSeF.

Wynik końcowy, potwierdzony ponownie przy zamknięciu etapu: **399/399 testów, 0 błędów, 0 pominięć**. Kompilacja całego rozwiązania: **0 błędów**, dwa dotychczasowe ostrzeżenia NU1510. Testy obejmują zapis i odczyt rozliczenia, zatwierdzenie oraz obliczanie należności i nadpłaty; korzystają z izolowanych baz testowych. `git diff --check` oraz kontrola nowych plików: poprawne. Migrację lokalnej bazy deweloperskiej wykonano bez usuwania danych; kod i logika biznesowa nie były zmieniane podczas zamknięcia etapu.

## Zmienione pliki

- `XKsef.Domain/SalesInvoices/InvoiceSettlement.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoice.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoiceTypes.cs`
- `XKsef.Domain/SalesInvoices/SalesInvoiceValidator.cs`
- `xKsef.Contracts/SalesInvoices/InvoiceSettlementContracts.cs`
- `xKsef.Contracts/SalesInvoices/SalesInvoiceContracts.cs`
- `xKesf.Application/SalesInvoices/SalesInvoiceContractMapper.cs`
- `xKesf.Application/SalesInvoices/SalesInvoiceIssuanceValidator.cs`
- `xKesf.Application/SalesInvoices/Snapshots/ApprovedSalesInvoiceData.cs`
- `xKesf.Application/SalesInvoices/Snapshots/SalesInvoiceApprovalSnapshot.cs`
- `xKesf.Application/SalesInvoices/Snapshots/SalesInvoiceSnapshotCapture.cs`
- `xKesf.Application/SalesInvoices/Snapshots/SalesInvoiceSnapshotDisclosure.cs`
- `xKesf.Application/SalesInvoices/Snapshots/SalesInvoiceSnapshotValidation.cs`
- `xKsef.Infrastructure/Persistance/Configuration/SalesInvoiceConfiguration.cs`
- `xKsef.Infrastructure/Migrations/20261128162009_AddSalesInvoiceSettlement.cs`
- `xKsef.Infrastructure/Migrations/20261128162009_AddSalesInvoiceSettlement.Designer.cs`
- `xKsef.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `xKsef.Tests/Domain/SalesInvoiceSettlementTests.cs`
- `xKsef.Tests/Infrastructure/SalesInvoiceSettlementModelTests.cs`
- `xKsef.Tests/Infrastructure/SalesInvoiceSnapshotModelTests.cs`
- `xKsef.Tests/Integration/SalesInvoiceSettlementTests.cs`
- `xKsef.Tests/Integration/SalesInvoiceTests.cs`
- `xKsef.Tests/Application/SalesInvoiceSnapshotTests.cs`
- `docs/analysis/FA3_SETTLEMENT_4B6C.md`

Brak nierozstrzygniętych kwestii wzoru rozliczenia. Docelowy generator XML powinien wykorzystać zapisane wyniki snapshotu i zrealizować wskazany wybór `DoZaplaty`/`DoRozliczenia`; jego implementacja pozostaje poza tym etapem.
