
# xKsef — kontekst techniczny projektu

Aktualizacja: 12.11.2026

## 1. Cel projektu

xKsef to wielodostępowa aplikacja do integracji z polskim Krajowym Systemem e-Faktur (KSeF).

Funkcjonalności:
- Uwierzytelnianie użytkowników aplikacji.
- Obsługa wielu firm (tenantów).
- Uwierzytelnianie w KSeF tokenem lub certyfikatem.
- Pobieranie faktur sprzedaży i zakupu na żądanie.
- Przechowywanie faktur w SQL Server.
- Przeglądanie faktur oraz podgląd XML i PDF.

Aktualnie interfejsem użytkownika jest WPF.
Docelowo planowana jest aplikacja przeglądarkowa.

## 2. Technologie

- .NET 10
- WPF, MVVM, CommunityToolkit.Mvvm
- ASP.NET Core Web API
- ASP.NET Core Identity i JWT
- Entity Framework Core 10
- SQL Server
- KSeF.Client 2.8.1
- PdfiumViewer
- Node.js, @akmf/ksef-fe-invoice-converter

## 3. Architektura

Główne komponenty:

- xKsef.Wpf — interfejs użytkownika.
- xKsef.Api — API aplikacji.
- xKsef.Domain — modele domenowe.
- xKsef.Infrastructure — dostęp do danych i integracje.
- xKsef.CredentialService — ochrona poświadczeń KSeF.

Podstawowy przepływ:

WPF -> xKsef.Api -> Infrastructure -> KSeF

Operacje związane z poświadczeniami:

WPF -> xKsef.Api -> CredentialService -> KSeF

Rzeczywiste zależności projektów należy zweryfikować
w plikach .csproj i rejestracji DI.

## 4. Uwierzytelnianie aplikacji

- ASP.NET Core Identity.
- JWT Bearer.
- BearerTokenHandler dodaje token JWT.
- TenantHeaderHandler dodaje X-Tenant-Id.
- TenantAuthorizationFilter sprawdza dostęp do tenanta.
- TenantContext przechowuje wybranego tenanta w WPF.

Role aplikacyjne są niezależne od uprawnień KSeF.

Użytkownik może mieć prawo do przeglądania faktur
w bazie, ale nie mieć prawa pobierania ich z KSeF.

## 5. Wielodostępność

- Tenant posiada identyfikator GUID.
- Użytkownik może należeć do wielu tenantów.
- Powiązanie użytkownika z tenantem: UserTenants.
- Ten sam NIP może występować w wielu tenantach.
- Faktury są izolowane przez TenantId.
- Unikalność numeru KSeF jest zapewniona w obrębie tenanta.

Docelowa skala:
- Około 10 000 użytkowników.
- Około 10 000 tenantów.
- Do około 1200 faktur rocznie na tenanta.

Priorytetem jest wydajność API i operacji na danych,
nie przedwczesna optymalizacja interfejsu.

## 6. CredentialService

Osobna usługa ASP.NET Core .NET 10.

Odpowiada za:
- Przechowywanie tokenów KSeF.
- Przechowywanie certyfikatów i kluczy prywatnych.
- Uwierzytelnianie w KSeF.
- Zarządzanie AccessToken i RefreshToken.

Bezpieczeństwo:
- AES-256-GCM.
- Osobny DEK dla każdego sekretu.
- DEK chroniony Windows DPAPI CurrentUser.
- Połączenie API z CredentialService przez mTLS.
- Brak przechowywania hasła do pliku PEM.
- Sekrety nie powinny trafiać do logów ani odpowiedzi API.

Aktualny mechanizm DPAPI jest prototypem lokalnym.
Przed wdrożeniem wieloserwerowym wymaga osobnej
decyzji architektonicznej.

Nie zmieniać mechanizmów kryptograficznych bez zgody.

## 7. Uwierzytelnianie KSeF

Zaimplementowano:
- Uwierzytelnianie tokenem KSeF.
- Uwierzytelnianie certyfikatem XAdES.
- Podpis ECDSA-SHA256.
- Pobieranie i przechowywanie tokenów sesyjnych.
- Odświeżanie AccessToken.
- Sprawdzanie ważności sesji.

Preferowane poświadczenie:
- Użytkownik wybiera token lub certyfikat.
- Identyfikator zapisywany jest jako
  UserTenants.PreferredKsefCredentialId.
- Preferencja jest odtwarzana po ponownym logowaniu.

Ostatnie testy:
- Uwierzytelnianie tokenem zakończone sukcesem.
- Import faktury z użyciem tokenu działa.
- Uwierzytelnianie certyfikatem działa.
- Import z certyfikatem przeszedł bez błędu,
  ale w ostatnim teście nie było nowych faktur.

## 8. Faktury

Faktury przechowywane są w SQL Server.

Najważniejsze dane:
- TenantId
- KsefNumber
- InvoiceNumber
- IssueDate
- Direction
- ImportedAt
- XmlBytes

Dokładny aktualny model należy sprawdzić w kodzie.

Funkcjonalności:
- Import faktur sprzedaży i zakupu.
- Zapisywanie nowych faktur.
- Obsługa duplikatów.
- Pobieranie XML.
- Generowanie PDF.
- Podgląd PDF w WPF.

Przykładowe endpointy:
- POST /api/invoices/import
- GET /api/invoices/{ksefNumber}/pdf
- GET /api/invoices/{ksefNumber}/xml

Sprawdzić aktualne definicje kontrolerów przed zmianami.

## 9. WPF

Architektura MVVM.

Istotne elementy:
- MainWindow.xaml
- MainViewModel
- TenantContext
- AuthenticationApiClient
- InvoiceApiClient
- TenantApiClient
- BearerTokenHandler
- TenantHeaderHandler

Funkcjonalności:
- Logowanie do aplikacji.
- Wybór tenanta.
- Lista faktur sprzedaży i zakupu.
- Pobieranie faktur z KSeF.
- Podgląd PDF.
- Menu konfiguracji poświadczeń KSeF.
- Wybór preferowanego poświadczenia.

Po zalogowaniu i wyborze tenanta aplikacja
powinna wczytywać faktury z lokalnej bazy
za domyślny okres ostatnich 30 dni.

Pobieranie faktur z KSeF następuje wyłącznie
na polecenie użytkownika.

## 10. Aktualny stan prac

Ostatni zakończony etap:
17.13.8.59.12.26.9.104.68

Stan na 10.11.2026:
- Obsługa tokenu i certyfikatu działa.
- Preferowane poświadczenie jest zapisywane.
- Preferencja odtwarzana jest po zalogowaniu.
- Import faktur działa.
- Podgląd PDF działa.

Nie zakładać, że każda ścieżka błędów
i każdy scenariusz produkcyjny zostały przetestowane.

## 11. Kolejność dalszych prac

Etap 1:
Uporządkowanie WPF.
- Analiza MainWindow i MainViewModel.
- Identyfikacja nieużywanego kodu.
- Uproszczenie struktury.
- Poprawa interfejsu.
- Zachowanie istniejącej funkcjonalności.

Etap 2:
Rozbudowa przeglądania faktur.
- Sumy netto, VAT i brutto.
- Filtrowanie według dat.
- Wyszukiwanie faktur.
- Sortowanie.
- Dalsze usprawnienia uzgodnione z użytkownikiem.

Etap 3:
Aplikacja przeglądarkowa.
- Wybór technologii: Angular lub Blazor.
- Ponowne wykorzystanie istniejącego API.
- Zachowanie izolacji tenantów i bezpieczeństwa.

## 12. Zasady dalszej pracy

- Przestrzegaj AGENTS.md.
- Pracuj jednym małym krokiem naraz.
- Przed zmianami przedstaw plan.
- Czekaj na zgodę użytkownika.
- Nie wprowadzaj nieuzgodnionych zmian.
- Nie zmieniaj bezpieczeństwa ani autoryzacji przy
  zwykłej refaktoryzacji UI.
- Zachowuj MVVM.
- Nie zawijaj niepotrzebnie linii C# i XAML.
- Nie wykonuj commitów ani push bez zgody.
- Po zmianach przedstaw diff i wyniki weryfikacji.

## 13. Zasada nadrzędna

Ten dokument opisuje stan projektu znany z wcześniejszych
prac i może zawierać nieaktualne szczegóły.

Aktualny kod źródłowy jest źródłem prawdy.

W przypadku rozbieżności między dokumentem a kodem:
- Zgłoś rozbieżność.
- Nie zgaduj.
- Nie poprawiaj automatycznie.
- Zaproponuj rozwiązanie i poczekaj na decyzję.
