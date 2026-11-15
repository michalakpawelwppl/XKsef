# Live HTTPS / API / SQL

Testy używają rzeczywistego API, produkcyjnego uwierzytelniania cookie, LocalDB i generatora PDF. Nie wysyłają dokumentów do KSeF ani nie wywołują importu. React jest serwowany przez istniejący `BrowserFrontend:Path`, z tego samego origin HTTPS co API.

Wymagania: Windows, .NET 10, SQL Server LocalDB `MSSQLLocalDB`, testowa baza `xKsef` z aktualnym schematem, `sqlcmd` w PATH, zależności npm i Chromium Playwright. API wymaga swoich istniejących user-secrets (m.in. klucza JWT i hasła użytkownika deweloperskiego). Nie kopiuj sekretów do repozytorium. Preflight wymaga manifestu dedykowanego procesu API i widoczności jego sesji SQL w `sys.dm_exec_sessions`. Brak uprawnień lub potwierdzenia kończy test przed rejestracją, bez obchodzenia tej kontroli.

Z katalogu głównego zbuduj aplikację:

```powershell
dotnet dev-certs https --check --trust
dotnet build xKsef.Api/xKsef.Api.csproj
Push-Location xKsef.Web
npm run build
Pop-Location
& ./xKsef.Web/scripts/start-live-api.ps1 -Origin https://localhost:7127
```

Launcher uruchamia istniejące API w Development/KSeF TEST, ustawia dla tego procesu LocalDB `xKsef`, unikalne `Application Name`, katalog zbudowanego React i lokalny endpoint HTTPS. Zapisuje PID, origin, nazwę połączenia i ścieżkę DLL do ignorowanego `artifacts/live-api-manifest.json`; manifest nie zawiera sekretów. Nie zmienia user-secrets ani trwałej konfiguracji środowiska. Nie dodaje endpointów do backendu.

Certyfikat musi obejmować wybraną nazwę hosta i być zaufany dla przeglądarki. Launcher sprawdza gotowość HTTPS bez pomijania TLS. Testy zachowują `ignoreHTTPSErrors: false`; nie zmieniaj produkcyjnej walidacji TLS. Przed kolejnym uruchomieniem zatrzymaj proces PID zapisany w manifeście (po sprawdzeniu, że nadal należy do tego API).

W drugim terminalu, z katalogu głównego:

```powershell
$env:XKSEF_SQL_TEST_EXPECTED_SERVER = (sqlcmd -S '(LocalDb)\MSSQLLocalDB' -d xKsef -E -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'));" | Out-String).Trim()
$env:XKSEF_WEB_LIVE_ORIGIN = 'https://localhost:7127'
$env:XKSEF_LIVE_API_MANIFEST = (Resolve-Path artifacts/live-api-manifest.json).Path
# Opcjonalnie: ścieżka do przekierowanego logu rzeczywiście uruchomionego API.
$env:XKSEF_LIVE_API_LOG = (Resolve-Path artifacts/safety-api.log).Path
Push-Location xKsef.Web
npm run test:live
Pop-Location
```

Podanie pliku logu włącza kontrolę obecności hasła syntetycznego, wartości cookie i nagłówków uwierzytelniania w logu. Bez pliku logu ta kontrola jest pomijana. Ślady i zrzuty ekranu live są wyłączone. Nie zapisujemy cookie ani storageState na dysku.

Przed zapisami fixture sprawdza konfigurację origin HTTPS, oczekiwanego serwera i manifest launchera. Potwierdza rzeczywiste połączenie wskazanego PID z unikalną nazwą aplikacji z bazą `xKsef` na oczekiwanej LocalDB przez `sys.dm_exec_sessions`. Sam manifest ani deklaracja operatora nie wystarczają do przejścia preflight. Wykrycie innej bazy, istniejących identyfikatorów fixture lub niezgodnej konfiguracji zatrzymuje przygotowanie przed rejestracją.

Fixture rejestruje dwóch użytkowników `m188-<losowy GUID>-a/b`, tworzy czterech tenantów i jedenaście faktur syntetycznych (w tym wspólny numer KSeF i XML FA(3)). Każda operacja SQL ponownie sprawdza bazę i przypięty podczas preflight identyfikator serwera. Sprzątanie usuwa wyłącznie konta z potwierdzoną rejestracją i dane o identyfikatorach tego przebiegu; działa również po częściowo udanym przygotowaniu i błędzie zamknięcia przeglądarki. Po udanym sprzątnięciu sprawdza brak własnych danych, a po błędzie zachowuje stan do ponowienia. Nie wykonuje migracji. Przy przerwaniu całego procesu lub utracie odpowiedzi rejestracji sprzątanie może wymagać ręcznej diagnostyki konkretnego prefiksu przebiegu; nie usuwaj kont o niepotwierdzonym pochodzeniu.

Pięć testów live działa kolejno, bez automatycznych ponowień. Oprócz trzech scenariuszy aplikacji obejmują odrzucenie błędnego serwera przed zapisami i sprzątnięcie po jednej udanej rejestracji. Jeden przebieg wykonuje pięć poprawnych logowań i trzy rejestracje, respektując istniejące limity API 5/minutę. Przed szybkim ponownym przebiegiem zaczekaj na odnowienie limitu albo uruchom ponownie własny proces API; nie zmieniaj limitera produkcyjnego.

Scenariusz spóźnionej odpowiedzi wstrzymuje dostarczenie rzeczywistej odpowiedzi HTTPS/SQL do React i celowo ignoruje anulowanie. Nie podmienia danych odpowiedzi. Pozostałe żądania przechodzą do API bez atrap. Wygaśnięcie sesji jest symulowane zmianą dat wyłącznie własnych rekordów `BrowserSessions`, z zachowaniem ograniczeń SQL. Chromium headless utrzymuje obie karty jako widoczne; przejście hidden → visible jest sterowane przez zdarzenia DOM, tak jak w standardowych testach wielu kart. Weryfikacja `/me` i reakcja na 401 pozostają rzeczywiste.
