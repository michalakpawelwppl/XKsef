# Przygotowanie xKsef do pracy w LAN

## Zakres i architektura

Etap 29.2.1 przygotowuje konfigurację WPF i jej publikację. Nie włącza nasłuchiwania LAN ani nie zmienia systemu. Konfiguracja serwera, DNS, certyfikatów i zapory opisana poniżej dotyczy kolejnych etapów i wymaga osobnego zatwierdzenia przed wykonaniem.

~~~text
Komputer klienta: WPF
    |
    | HTTPS, port 7127
    v
Komputer serwera: API
    |-- HTTPS/mTLS przez localhost:7260 --> CredentialService
    |-- lokalne połączenie ------------> baza xKsef
    CredentialService ----------------> lokalna baza xKsefCredentials
~~~

WPF komunikuje się wyłącznie z API. Nie potrzebuje dostępu do SQL Server ani CredentialService. Zachowujemy istniejące bazy, konta uruchomieniowe, JWT, autoryzację tenantów, mTLS i DPAPI CurrentUser. Nie zmieniamy importu ani sposobu przechowywania poświadczeń i sesji.

Wszystkie oznaczenia w nawiasach kątowych są symbolami do zastąpienia po ustaleniu konfiguracji. Nie opisują rzeczywistej nazwy ani adresu komputera.

## Stabilny adres serwera

Przed konfiguracją należy ustalić nazwę DNS serwera oraz zapewnić jej rozwiązywanie na obu komputerach. Zalecana jest rezerwacja adresu w DHCP i odpowiedni wpis w lokalnym DNS. Dla małej instalacji alternatywą jest osobno zatwierdzony wpis w pliku hosts.

WPF będzie korzystać z adresu `https://<nazwa-dns-serwera>:7127/`. Nie trzeba zmieniać nazwy komputera, jeśli można zastosować alias DNS. Używana nazwa musi występować w SAN certyfikatu HTTPS. Połączenie przez sam adres IP wymagałoby odrębnego wpisu IP w SAN; preferujemy nazwę DNS.

## Konfiguracja Kestrel wyłącznie dla API

Po przygotowaniu certyfikatu można ustawić poniższą konfigurację lokalnie dla projektu xKsef.Api, np. w jego User Secrets. Nie należy stosować globalnych zmiennych środowiskowych, które zmieniłyby również nasłuchiwanie CredentialService. Do repozytorium nie dodajemy danych prywatnego certyfikatu ani lokalnej konfiguracji serwera.

~~~json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:7127",
        "Certificate": {
          "Subject": "<nazwa-dns-serwera>",
          "Store": "My",
          "Location": "CurrentUser",
          "AllowInvalid": false
        }
      },
      "Http": {
        "Url": "http://localhost:5102"
      }
    }
  }
}
~~~

Certyfikat w tym przykładzie ma Subject `CN=<nazwa-dns-serwera>`. Wartość Subject należy dopasować do rzeczywiście przygotowanego certyfikatu. Kestrel odczytuje go z magazynu konta uruchamiającego API; konto musi mieć dostęp do klucza prywatnego.

HTTPS na porcie 7127 obejmuje interfejsy LAN i loopback, więc lokalny WPF nadal może korzystać z https://localhost:7127/. Nie dodajemy drugiego, nakładającego się nasłuchu HTTPS na tym samym porcie. HTTP pozostaje wyłącznie na localhost:5102. Ruch WPF przez LAN zawsze używa HTTPS.

Sekcja Kestrel:Endpoints zastępuje adresy z ustawienia Urls, w tym adresy wybrane przez launchSettings.json. Dlatego przykład obejmuje oba endpointy. Samo wpisanie nazwy DNS jako adresu nasłuchu nie zastępuje konfiguracji DNS ani ograniczenia dostępu zaporą.

AllowedHosts nie steruje nasłuchiwaniem i nie zastępuje zapory. Można go później zawęzić w lokalnej konfiguracji API do localhost i zatwierdzonych nazw DNS.

CredentialService pozostaje na https://localhost:7260 oraz http://localhost:5221. Nie zmieniamy jego profili, certyfikatów ani transportu mTLS. Nie konfigurujemy zdalnego dostępu do SQL Server.

## Certyfikat HTTPS i zaufanie

Certyfikat serwerowy API musi:

- mieć zastosowanie Server Authentication i aktualny okres ważności;
- zawierać w SAN ustaloną nazwę LAN oraz localhost;
- zawierać również pełną nazwę DNS, jeśli będzie używana przez klienta;
- posiadać klucz prywatny dostępny wyłącznie uprawnionemu kontu na serwerze.

Preferowany jest certyfikat wystawiony przez dostępne firmowe lub lokalne CA. Dla instalacji developerskiej można przygotować osobny certyfikat samopodpisany i jawnie zaufać jego publicznej części na serwerze i komputerze WPF.

Na klienta przenosimy wyłącznie publiczny certyfikat lub publiczny certyfikat CA. Przed dodaniem zaufania należy potwierdzić odcisk przez niezależne porównanie. Prywatny klucz API pozostaje na serwerze. Certyfikat HTTPS API jest odrębny od certyfikatu klienta mTLS i certyfikatów uwierzytelniania KSeF.

Nie ustawiamy AllowInvalid=true ani callbacków pomijających walidację. Zaufanie musi działać w kontekście użytkownika Windows uruchamiającego WPF. Zmiany magazynów certyfikatów wykonujemy dopiero po osobnym zatwierdzeniu.

## Zapora Windows

Przed zmianą należy sprawdzić aktywny profil sieci oraz istniejące reguły, w szczególności szerokie zezwolenia dla aplikacji lub dotnet.exe. Dodanie wąskiej reguły nie ogranicza wcześniejszych, szerszych zezwoleń.

Docelowe zezwolenie przychodzące powinno obejmować:

- protokół TCP i lokalny port 7127;
- właściwy profil sieci, zwykle Private;
- konkretną podsieć LAN albo adres uprawnionego komputera WPF;
- w razie potrzeby konkretny interfejs sieciowy.

Nie otwieramy dla LAN portu HTTP 5102, portów CredentialService 7260/5221 ani portów SQL Server. Nie konfigurujemy przekierowania portów na routerze. Dodanie lub zmiana reguł zapory wymaga osobnego zatwierdzenia.

## Konfiguracja WPF

Plik appsettings.json w repozytorium zachowuje domyślny adres lokalny:

~~~json
{
  "Api": {
    "BaseUrl": "https://localhost:7127/"
  }
}
~~~

Dopiero w kopii publikacji na komputerze klienta należy ustawić:

~~~json
{
  "Api": {
    "BaseUrl": "https://<nazwa-dns-serwera>:7127/"
  }
}
~~~

Konfiguracja jest odczytywana względem AppContext.BaseDirectory przez Host.CreateDefaultBuilder().UseContentRoot(...). Uruchomienie ze skrótu lub z innym katalogiem roboczym nie zmienia miejsca szukania pliku.

Zachowane są standardowe źródła konfiguracji hosta, w tym appsettings.json, appsettings.{Environment}.json i zmienne środowiskowe. Zmienna Api__BaseUrl może nadpisać wartość pliku, dlatego należy uwzględnić ją w diagnostyce. Nie dodajemy nowego mechanizmu argumentów wiersza poleceń.

Zmiana adresu nie wymaga kompilacji, ale wymaga restartu WPF. Nowa publikacja może zastąpić plik konfiguracji w katalogu docelowym; przed aktualizacją należy zachować lokalną konfigurację i przywrócić właściwy adres.

## Plan późniejszej publikacji

Publikacja i uruchomienie na drugim komputerze są osobnym etapem. Dla 64-bitowego Windows proponowany początkowy wariant to folder Release, win-x64, self-contained:

~~~powershell
dotnet publish xKsef.Wpf/xKsef.Wpf.csproj -c Release -r win-x64 --self-contained true -o ./artifacts/wpf
~~~

Architekturę należy dostosować do komputera klienta. Na początek nie włączamy single-file ani trimming. Trzeba przenieść cały wynik publikacji, w tym biblioteki natywne podglądu PDF i appsettings.json, a nie tylko plik EXE. Projekt zachowuje CopyToOutputDirectory=PreserveNewest i dodaje CopyToPublishDirectory=PreserveNewest dla konfiguracji.

Na komputerze klienta nie są potrzebne SQL Server, CredentialService ani Node.js. Generowanie PDF pozostaje po stronie API; lokalny podgląd korzysta z zależności dostarczonych z WPF. Pakiet klienta nie może zawierać klucza podpisującego JWT, serwerowych connection stringów ani prywatnych kluczy usług.

## Profile TEST i PROD

Istniejące profile TEST/PROD oraz zestawy xKsef TEST i xKsef PROD pozostają bez zmian. Wybór KSeF nadal wynika z Ksef__Environment i jest niezależny od Debug/Release. ASPNETCORE_ENVIRONMENT pozostaje Development, z dotychczasowym zachowaniem DevelopmentUserSeeder.

Lokalna konfiguracja endpointów API będzie wspólna dla tych profili. Nie zmienia wyboru KSeF. Zestawy Visual Studio uruchamiają również lokalny WPF; można równolegle korzystać z WPF na drugim komputerze.

Przed zmianą zestawu należy zatrzymać poprzednie uruchomienie. Kontrola z kroku 29.1.3 nadal blokuje niezgodne środowiska API i CredentialService. Nie wprowadzamy separacji zapisanych sesji TEST/PROD; po przełączeniu mogą wystąpić zaakceptowane błędy uwierzytelniania.

Samo uruchomienie usług w PROD może nawiązać połączenie z produkcyjnym KSeF podczas inicjalizacji SDK. Weryfikację konfiguracji w tym etapie wykonujemy bez uruchamiania usług. Późniejsze próby sieciowe zaczynamy od TEST.

## Kolejność późniejszego testowania

Po zatwierdzeniu i wykonaniu konfiguracji systemowej:

1. Sprawdź rozwiązywanie ustalonej nazwy na obu komputerach.
2. Uruchom API i CredentialService w TEST. Potwierdź nasłuch HTTPS API na 7127 oraz wyłącznie lokalny nasłuch CredentialService. Sam porządek projektów w zestawie Visual Studio nie gwarantuje gotowości usług.
3. Sprawdź lokalny dostęp do API przez https://localhost:7127/ z poprawną walidacją certyfikatu.
4. Na drugim komputerze sprawdź TCP do portu 7127, np. przez Test-NetConnection. Ten test nie sprawdza jeszcze TLS.
5. Wykonaj zwykłe żądanie HTTPS do `https://<nazwa-dns-serwera>:7127/api/tenants` bez JWT. Oczekiwana odpowiedź 401, bez błędu certyfikatu, potwierdza przejście TLS i dotarcie do chronionego API. Żądanie to nie wymaga wywołania KSeF.
6. Nie używaj opcji pomijania walidacji, np. curl -k lub SkipCertificateCheck. Przy błędzie sprawdź nazwę SAN, okres ważności i łańcuch zaufania.
7. Ustaw adres w opublikowanym WPF i sprawdź logowanie, wybór tenanta, lokalne listy, podsumowania i podgląd PDF.
8. Sprawdź uruchomienie WPF ze skrótu i z katalogiem roboczym innym niż katalog aplikacji.
9. Potwierdź brak dostępu z klienta do portów CredentialService i SQL Server oraz zachowanie lokalnego WPF.
10. Dopiero w osobno zatwierdzonym zakresie sprawdź operacje KSeF TEST. Nie uruchamiaj PROD w celu weryfikacji połączenia LAN.

## Dokumentacja źródłowa

- [Konfiguracja endpointów i certyfikatów Kestrel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0)
- [Konfiguracja .NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host)
- [Reguły zapory Windows](https://learn.microsoft.com/en-us/powershell/module/netsecurity/new-netfirewallrule?view=windowsserver2025-ps)
- [Profile uruchomienia w tym repozytorium](KSEF_LAUNCH_PROFILES.md)
