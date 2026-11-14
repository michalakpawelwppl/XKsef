# Profile uruchomienia KSeF w Visual Studio 2026

Współdzielony plik XKsef.slnLaunch, obok XKsef.slnx, zawiera dwa zestawy:

| Zestaw | Profil CredentialService | Profil API | WPF |
|---|---|---|---|
| xKsef TEST | TEST | TEST | Zwykłe uruchomienie projektu |
| xKsef PROD | PROD | PROD | Zwykłe uruchomienie projektu |

## Wybór zestawu

1. Otwórz XKsef.slnx w Visual Studio 2026.
2. Wybierz xKsef TEST albo xKsef PROD z listy obok przycisku uruchamiania.
3. Jeśli zestawów nie widać, otwórz menu rozwiązania → Configure Startup Projects. W razie potrzeby włącz Enable Multi-Project Launch Profiles w Tools → Options → Preview Features. Nazwy i położenie opcji mogą zależeć od wersji i języka Visual Studio.
4. Przed zmianą zestawu zatrzymaj poprzednie uruchomienie, włącznie z WPF. Oba zestawy wykorzystują te same porty.
5. Uruchom wybrany zestaw przez F5 lub Ctrl+F5.

Zestawy są zapisane w formacie współdzielonym (odpowiednik Share Profile). Lokalny XKsef.slnLaunch.user pozostaje niezależny i jest ignorowany przez Git; nie należy go dodawać do repozytorium. Dotychczasowe profile http i https pozostają dostępne.

## Znaczenie ustawień

Profile TEST i PROD w Properties/launchSettings.json obu usług ustawiają odpowiednio Ksef__Environment=TEST albo Ksef__Environment=PROD. Wspólny resolver wybiera adres KSeF. Sprzeczny Ksef:BaseUrl pochodzący np. z User Secrets lub innego źródła konfiguracji spowoduje błąd uruchomienia; profil nie ukrywa takiego konfliktu.

Wybór KSeF jest niezależny od Debug/Release. W obu profilach pozostaje ASPNETCORE_ENVIRONMENT=Development, więc zachowanie DevelopmentUserSeeder i OpenAPI jest dotychczasowe. Nazwa PROD oznacza wybór produkcyjnego KSeF, a nie zmianę hosta na ASP.NET Core Production.

Adresy pozostają bez zmian:

- API: https://localhost:7127;http://localhost:5102.
- CredentialService: https://localhost:7260;http://localhost:5221.
- WPF korzysta z dotychczasowej konfiguracji adresu API.

W pliku zestawu projekty są wymienione w kolejności CredentialService → API → WPF. Jest to kolejność żądanych uruchomień, a nie gwarancja zakończenia inicjalizacji usług. Nie ma mechanizmu oczekiwania na ich gotowość.

## PROD i wspólna baza

Samo uruchomienie zestawu PROD może nawiązać połączenie z produkcyjnym KSeF, ponieważ usługi inicjalizują kryptografię SDK przy starcie. Do sprawdzenia składni profili wystarcza walidacja statyczna; nie należy w tym celu uruchamiać PROD.

Profile nie zmieniają baz danych, poświadczeń ani zapisanych sesji. Nie zapewniają separacji faktur i sesji TEST/PROD ani kontroli zgodności środowisk między niezależnie uruchomionymi usługami. Restart nie usuwa sesji KSeF zapisanych w bazie.

Opis formatu i opcji Share Profile: [dokumentacja Microsoft](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-set-multiple-startup-projects?view=visualstudio).
