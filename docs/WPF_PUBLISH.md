# Publikacja xKsef.Wpf na Windows x64

## Profil i katalog wynikowy

Współdzielony profil xKsef.Wpf/Properties/PublishProfiles/Folder-win-x64.pubxml publikuje do folderu jako Release, win-x64, self-contained. Single-file i trimming są wyłączone. Profil nie wybiera środowiska KSeF i nie zawiera adresu LAN ani sekretów.

Z katalogu rozwiązania:

~~~powershell
dotnet publish xKsef.Wpf/xKsef.Wpf.csproj -c Release -p:PublishProfile=Folder-win-x64
~~~

W Visual Studio wybierz projekt xKsef.Wpf → Publish → Folder-win-x64. Nie wybieraj ClickOnce ani instalatora.

Domyślny katalog publikacji to sąsiedni względem repozytorium folder XKsef-publish/Wpf/win-x64. Ścieżka jest wyliczana względem katalogu projektu, a nie bieżącego katalogu roboczego. PublishDir i PublishUrl wskazują ten sam wynik dla CLI i Visual Studio.

Do weryfikacji używaj nowego, pustego katalogu. Profil nie usuwa istniejących plików; ponowna publikacja może nadpisać pliki, ale pozostawić dodatkowe pliki z wcześniejszej paczki. Inny katalog można jawnie wskazać przez -o. Nie publikuj bezpośrednio do działającej instalacji.

## Zawartość paczki i PDF

Przenoś cały wynik publikacji, z podkatalogami. Należy zachować co najmniej:

- xKsef.Wpf.exe, xKsef.Wpf.dll oraz pliki deps.json i runtimeconfig.json;
- appsettings.json;
- dołączony runtime .NET i biblioteki WPF, np. coreclr.dll, hostfxr.dll, System.Private.CoreLib.dll i PresentationFramework.dll;
- biblioteki aplikacji, PdfiumViewer.dll, System.Drawing.Common.dll i pdfium.dll dla x64.

PdfiumViewer.Net.WPF 3.0.4 używa pakietu bblanchon.PDFium.Win32 130.0.6721. Natywne zasoby wybiera standardowy mechanizm publikacji SDK dla win-x64. Nie kopiuj ręcznie DLL z innych architektur ani przypadkowych wersji z systemu.

Self-contained zawiera wymagany runtime .NET, więc osobna instalacja .NET Desktop Runtime nie jest potrzebna. Nadal wymagany jest zgodny Windows x64 z zależnościami systemowymi. Sprawdź wersję i edycję systemu przez winver: samo określenie Windows 10 x64 nie rozstrzyga oficjalnego wsparcia .NET 10. [Obsługiwane wersje Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions)

Publikacja nie powinna zawierać konfiguracji API/CredentialService, connection stringów serwera, klucza podpisującego JWT ani prywatnych certyfikatów. Nie kopiuj do niej User Secrets, plików PFX/PEM/KEY ani katalogów projektów serwerowych.

## Przeniesienie na drugi komputer

1. Po zakończonej publikacji skopiuj cały katalog wynikowy do nowego lokalnego katalogu na komputerze Windows x64.
2. W skopiowanym appsettings.json zmień wyłącznie Api:BaseUrl na https://primesoft639:7127/. Plik w repozytorium i początkowej publikacji pozostaje z adresem https://localhost:7127/.
3. Sprawdź, czy zmienna środowiskowa Api__BaseUrl nie nadpisuje ustawienia pliku.
4. Uruchom xKsef.Wpf.exe z lokalnego katalogu. Skrót może mieć inny katalog roboczy, ponieważ konfiguracja jest odczytywana względem AppContext.BaseDirectory.
5. Zaloguj się, wybierz tenant, pobierz lokalną listę faktur i sprawdź podgląd PDF. Na początku nie uruchamiaj importu ani uwierzytelniania KSeF w ramach próby publikacji.
6. Zmiana adresu API wymaga restartu WPF, ale nie kompilacji. Przed aktualizacją aplikacji zachowaj lokalną konfigurację adresu.

API, CredentialService, SQL Server i generator PDF pozostają na primesoft639. Komputer WPF komunikuje się tylko z API przez HTTPS i korzysta ze sprawdzonego już zaufania certyfikatu. Nie wyłączaj walidacji HTTPS.

Lokalny test ładowania i renderowania PDFium na komputerze publikującym nie zastępuje próby interfejsu oraz PDF na docelowym Windows 10. Błąd ładowania PDFium wymaga sprawdzenia kompletności folderu i architektury; nie należy naprawiać go przez mieszanie wersji DLL.

Szczegóły konfiguracji serwera: [LAN_SETUP.md](LAN_SETUP.md). Wybór KSeF TEST/PROD nadal odbywa się przez dotychczasowe profile usług w Visual Studio; publikacja WPF ich nie uruchamia ani nie przełącza.
