# xKsef.Web — szkielet przeglądarkowy M14.2

React + TypeScript + Vite, React Router i AG Grid Community (MIT). Logowanie korzysta z istniejącego API cookie M13.2. Widoki sprzedaży i zakupów pobierają faktury zapisane w bazie przez `GET /api/invoices/query`. Publiczny prototyp M14.1 nadal używa danych syntetycznych. Frontend nie wywołuje importu ani KSeF.

## Uruchomienie całego rozwiązania

Wymagania: .NET 10, Node.js 22.12+ z linii 22 lub LTS 24+, istniejąca developerska konfiguracja API/SQL Server, trwałe klucze Data Protection oraz zaufany certyfikat HTTPS dla localhost. M14.2 nie wymaga migracji.

1. Z katalogu repozytorium zbuduj frontend:

```powershell
npm --prefix xKsef.Web ci
npm --prefix xKsef.Web run build
```

2. W osobnym terminalu uruchom dotychczasową lokalną usługę poświadczeń, jeśli potrzebujesz całego rozwiązania. Rejestracja, logowanie i wybór tenanta nie wymagają jej dostępności:

```powershell
$env:Ksef__WarmupOnStartup = 'false'
dotnet run --project xKsef.CredentialService --launch-profile TEST
```

3. W drugim terminalu uruchom API, wskazując **wyłącznie katalog dist**:

```powershell
$env:BrowserFrontend__Path = (Resolve-Path xKsef.Web/dist).Path
$env:Ksef__WarmupOnStartup = 'false'
dotnet run --project xKsef.Api --launch-profile TEST
```

4. Otwórz `https://localhost:7127/`. Jeżeli lokalna konfiguracja Kestrel nadpisuje adres z profilu, użyj jej istniejącego adresu HTTPS. Nie uruchamiaj drugiego procesu na zajętym porcie. Możesz również ustawić powyższe zmienne w konfiguracji uruchomienia Visual Studio, bez zmieniania współdzielonych profili TEST/PROD.

Frontend i `/api/*` są udostępniane przez ten sam origin API. Opcjonalna konfiguracja `BrowserFrontend:Path` umożliwia hostowanie z dowolnego katalogu wdrożenia; ścieżka musi być bezwzględna i zawierać zbudowany `index.html`. Bez tego ustawienia API działa jak wcześniej. Hosting SPA nigdy nie obsługuje nieznanych ścieżek `/api` ani żądań zapisu. Nie wskazuj katalogu źródłowego ani katalogu z sekretami.

Samo `npm --prefix xKsef.Web run dev` uruchamia frontend na `http://127.0.0.1:5173`. Pod `/prototype/invoices` działa niezależny prototyp. Logowanie wymaga wariantu HTTPS ze wspólnym originem opisanego wyżej; Vite nie jest skonfigurowane jako niezależny klient API. Nie wyłączamy walidacji TLS, nie kopiujemy certyfikatów prywatnych i nie dodajemy CORS.

Po przebudowaniu frontendu odśwież przeglądarkę. WPF i CredentialService nie są modyfikowane przez M14.2. Uwierzytelnianie KSeF i import nie są wykonywane przez ten frontend.

## Sesja, uprawnienia i moduły

- `/login` i `/register`: `/api/browser-auth/csrf`, `login`, `register`, `me`, `logout`; rejestracja wraca do logowania bez automatycznego zalogowania.
- Cookie HttpOnly/Secure pozostaje zarządzane przez API. Brak JWT, identyfikatorów sesji, CSRF i danych użytkownika w localStorage/sessionStorage.
- CSRF jest przechowywane tylko w pamięci klienta. Jest pobierane przed zapisem i odświeżane po zmianie tożsamości oraz wylogowaniu. Nie ma automatycznego powtarzania POST ani innych zapisów.
- `ApiClient.request(path, { scope: 'anonymous' | 'session' | 'tenant' })` używa wyłącznie `/api/*` tego samego originu, cookie i `cache: no-store`. Tylko zakres tenant dodaje uchwycony `X-Tenant-Id`; zapisy dodają `X-CSRF-TOKEN`. Nigdy nie dodaje Authorization.
- `RequestContext` posiada osobne generacje sesji i tenanta oraz AbortController. Żądania tenantowe są anulowane po zmianie tenanta; wszystkie żądania po zmianie sesji. Aktualność jest sprawdzana również po odczytaniu treści odpowiedzi. A → B → A nie przywraca ważności starego żądania. Operacje sesji mają osobnych właścicieli stanu busy.
- `BrowserSessionStore` przechowuje wyłącznie aktualny stan w pamięci; wylogowanie/401 natychmiast usuwa dane użytkownika i tenantów. Nieudane wylogowanie pokazuje błąd potwierdzenia po stronie serwera. Odpowiedź 403 tenantowa usuwa wybór i odświeża dostęp, bez powtarzania operacji biznesowej.
- `/app/invoices` przekierowuje do `/app/invoices/sales`; zakupy są pod `/app/invoices/purchase`. Domyślny zakres to ostatnie 30 dni kalendarzowych, łącznie z dzisiejszym, według daty wystawienia. Formularz „Filtruj” stosuje zakres „Data od”–„Data do” i rodzaj daty: wystawienia lub przyjęcia do KSeF. Dla przyjęcia do KSeF żądanie przekazuje `timeZoneId=Europe/Warsaw`; dni nie są przeliczane na UTC w przeglądarce. Niepoprawny zakres blokuje zapytanie. „Przywróć domyślne” przywraca i stosuje domyślny zakres, a „Odśwież listę” ponawia ostatnie zastosowane zapytanie. Wszystkie te operacje wyłącznie odczytują lokalną bazę. Data sprzedaży nie jest jeszcze dostępna.
- Podsumowania dotyczą wszystkich rekordów po filtrach i wyszukiwaniu AG Grid, również poza widocznym obszarem, niezależnie od checkboxów. Kwoty są grupowane według waluty; VAT w PLN nie jest doliczany. Brak dowolnej wartości netto/VAT daje niepełną sumę i licznik braków, a nie zero.
- API zachowuje pola liczbowe dla WPF i dodaje do `InvoiceDto` pola `grossAmountExact`, `netAmountExact`, `vatAmountExact`, `vatAmountPlnExact`: teksty z kropką i dwoma miejscami, zgodne z bazowym `decimal(18,2)`, lub `null` dla brakujących kwot. Podsumowania parsują wyłącznie te teksty do jednostek 1/100 w `BigInt`, bez sumowania `number` i bez nowych zależności. Wymagane jest uruchomienie API ze zaktualizowanym DTO; frontend odrzuca brakujące lub niezgodne dokładne pola zamiast zgadywać kwoty. Istniejące filtry liczbowe AG Grid nadal korzystają z pól `number`.
- Tenant jest wybierany w pamięci bieżącej karty; moduł jest odtwarzany po każdej zmianie generacji, więc faktury, filtry i zaznaczenia nie przechodzą do innego kontekstu. Hook anuluje poprzednie żądania i odrzuca nieaktualne odpowiedzi. Backend nadal autoryzuje każde żądanie.
- „Odśwież dostęp” aktualizuje listę tenantów i rolę. Brak przypisań pokazuje osobny pusty stan. Na tym etapie tworzenie tenantów i zarządzanie ich użytkownikami pozostaje w WPF.
- Przyszłe moduły są nieaktywne. Kod formularzy i faktur jest ładowany na żądanie. Kolejne moduły powinny używać tego samego klienta HTTP i zakresu tenant, zamiast własnych fetch/cache.
- `/prototype/invoices` pozostaje publiczną demonstracją na danych syntetycznych bez żądań API. Nie udostępnia danych użytkownika ani faktur z bazy.

## Testy i build

```powershell
npm --prefix xKsef.Web run licenses:check
npm --prefix xKsef.Web test
npm --prefix xKsef.Web run build
$env:PLAYWRIGHT_CHANNEL = 'msedge'
npm --prefix xKsef.Web run test:e2e
```

Alternatywnie w katalogu `xKsef.Web`: `npm run verify`. Bez `PLAYWRIGHT_CHANNEL` wymagane jest Chromium Playwright (`npx playwright install chromium`). Zestaw standardowy uruchamia zbudowane dist na lokalnym HTTP z mockami kontraktów API; testy M14.1 nie używają API. Raport HTML: `playwright-report/index.html`; zrzuty i pomiary w `test-results/`. Są ignorowane przez Git.

Testy HTTP/magazynu obejmują CSRF, błędy 400/401/403/429/503, brak ponowień, anulowanie, spóźnione odpowiedzi, A → B → A i własność busy. Testy przeglądarkowe obejmują formularze, zmianę tenanta, wygasanie i czyszczenie stanu. Rzeczywiste flagi cookie, SQL ITicketStore, autoryzację JWT, CSRF i izolację baz weryfikuje dodatkowo regresja `xKsef.Tests`.

Zestaw opcjonalny `npm run test:live` wymaga jawnego `XKSEF_WEB_LIVE_ORIGIN=https://localhost:...` oraz izolowanego konta testowego `XKSEF_WEB_TEST_LOGIN`. Korzysta z rzeczywistego API i LocalDB, tworzy konto i dwa tenanty, bez wywołań KSeF. Wymaga osobnego kontrolowanego przygotowania i sprzątnięcia **wyłącznie własnych rekordów**. Nie uruchamia się w standardowym verify. Walidacja HTTPS pozostaje włączona.

## Zakres prototypu

- Sprzedaż i zakup: 10 kolumn biznesowych plus kolumna wyboru wierszy.
- Sortowanie nagłówkami, filtry kolumnowe tekst/liczba/data oraz wyszukiwanie we wszystkich kolumnach.
- Wielokrotny wybór wierszy; checkbox nagłówka obejmuje bieżący przefiltrowany wynik.
- Standardowa nawigacja AG Grid: strzałki, Tab / Shift+Tab i wybór spacją.
- Daty `dd.MM.yyyy`, daty przyjęcia lokalnie `dd.MM.yyyy HH:mm`; kwoty pl-PL z dwiema cyframi po przecinku i wyrównaniem do prawej.
- `null` daje pustą komórkę; rzeczywiste zero jest prezentowane jako `0,00`. Obsługiwane są wartości ujemne i różne kody walut. Prototyp nie oblicza podsumowań finansowych.
- Pola daty bez godziny nie przechodzą przez interpretację UTC. Oryginalny tekst daty z offsetem pozostaje w modelu bez zmian.
- Zbiory 1 000 / 10 000 / 100 000 rekordów; stała wysokość obszaru tabeli i wirtualizacja wierszy oraz kolumn.
- Reset widoku czyści sortowanie, filtry, wyszukiwanie i wybór. Zmiana kierunku lub wielkości zbioru tworzy osobny widok.
- Brak implementacji eksportu XLSX/CSV. Nie jest rejestrowany moduł eksportu tabeli.

Wirtualizacja ogranicza DOM, ale cały syntetyczny zbiór pozostaje w pamięci przeglądarki. Nie jest to deklaracja gotowości do pobierania dowolnie dużych zbiorów przez obecne API. Późniejszy adapter danych może korzystać z darmowego Infinite Row Model lub stronicowania, po uzgodnieniu kontraktu API. Prototyp nadal nie pobiera rzeczywistych faktur.

## Granice komponentów

- `src/shared/table/TableColumn.ts` — generyczny opis kolumn i wartości, bez zależności od modelu faktur ani typów AG Grid.
- `src/shared/table/DataTable.tsx` i `gridColumns.ts` — integracja Community, wybór wierszy, filtry, wirtualizacja i mapowanie prezentacji.
- `src/shared/formatting/polish.ts` — formatowanie i interpretacja danych wejściowych filtrów.
- `src/modules/invoices/Invoice.ts` — model tylko do odczytu.
- `src/modules/invoices/invoiceColumns.ts` — kolejność kolumn, wybór nabywcy/sprzedawcy i prezentacja faktur.
- `src/modules/invoices/InvoiceTable.tsx` — wyszukiwanie i liczniki widoku faktur.
- `src/modules/invoices/demoInvoices.ts` — dane syntetyczne, również korekty, zera i braki metadanych.
- `src/app` — routing i AppShell; `src/platform` — klient HTTP, sesja i kontrola kontekstu.

Inny moduł przekazuje własne `rows`, `TableColumn<T>[]` i stabilne `getRowId`. Test adaptera używa innego modelu (`account`, `balance`), bez pól faktur. W przyszłej integracji identyfikator wiersza musi uwzględniać właściwy kontekst tenanta; identyfikatory `demo-*` służą wyłącznie temu prototypowi.

Wszystkie kwoty są tu prezentowane, a nie księgowane. JavaScript `number` nie zastępuje .NET `decimal`; dokładny kontrakt edycji kwot i sumowania należy ustalić w odpowiednim etapie backendu. Filtry w tym prototypie są lokalne i nie korzystają z podsumowań API.

## Porównanie wyłącznie darmowych bibliotek

| Biblioteka | Licencja | Dopasowanie do obecnych wymagań |
| --- | --- | --- |
| AG Grid Community | MIT | Gotowy interfejs, sortowanie, filtry, wybór wierszy, klawiatura i wirtualizacja. Najmniejszy zakres własnej logiki potrzebnej w tym prototypie. |
| TanStack Table | MIT | Logika headless. Duża swoboda, ale interfejs, obsługa klawiatury i integracja wirtualizacji (np. TanStack Virtual, MIT) wymagają własnej pracy. |
| MUI X Data Grid Community | MIT | Gotowy interfejs; obowiązkowe stronicowanie i maksymalnie 100 wierszy na stronie. Mniej pasuje do ciągłego przewijania dużej listy. |

Rekomendacja: AG Grid Community. TanStack Table jest alternatywą, jeśli później konieczna będzie bardzo niestandardowa budowa tabel. Nie są instalowane pozostałe biblioteki ani analizowane warianty płatne.

Źródła sprawdzone 14.11.2026:
- https://www.ag-grid.com/react-data-grid/row-models/
- https://www.ag-grid.com/react-data-grid/keyboard-navigation/
- https://tanstack.com/table/latest/docs/overview
- https://mui.com/x/react-data-grid/pagination/

## Licencje i zależności

Wszystkie zależności produkcyjne są na MIT, w tym React, React DOM, Scheduler, AG Grid Community, integracja React i polskie tłumaczenie AG Grid. Pełną listę, również pozostałych zależności pośrednich, zawiera plik notices.
Pełne informacje prawnoautorskie z zainstalowanych pakietów są zachowane w `public/third-party-notices.txt` i kopiowane do `dist` przez Vite.

`npm run licenses:check` sprawdza licencje całego lockfile, również opcjonalnych zależności dla innych platform. Narzędzia developerskie mają licencje MIT, Apache-2.0, ISC, BSD-3-Clause, 0BSD oraz MPL-2.0. MPL-2.0 dotyczy narzędzia budowania CSS `lightningcss`; nie jest ono biblioteką uruchomieniową aplikacji. Licencje pozwalają na użycie komercyjne; zachowujemy ich warunki i nie modyfikujemy kodu zależności. Informacje o MPL: https://www.mozilla.org/en-US/MPL/2.0/FAQ/.

Po aktualizacji pakietów należy przejrzeć licencje i wykonać `npm run licenses:write`, a następnie pełne `npm run verify`.
Licencje bibliotek nie zmieniają licencji własnego kodu xKsef (`private`, `UNLICENSED` w package.json).
