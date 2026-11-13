
# xKsef — instrukcje dla Codex

## Projekt
xKsef to aplikacja .NET 10 do integracji z Krajowym Systemem e-Faktur (KSeF).

Rozwiązanie zawiera aplikację WPF, ASP.NET Core Web API, warstwę Infrastructure, Domain oraz osobny CredentialService.

## Zasady współpracy
1. Pracujemy małymi krokami — jedna zmiana funkcjonalna naraz.
2. Przed modyfikacją kodu przedstaw plan i poczekaj na akceptację użytkownika.
3. Nie zmieniaj architektury bez wyraźnej zgody.
4. Nie usuwaj istniejących metod, klas ani właściwości bez sprawdzenia ich użycia.
5. Zachowuj dotychczasowe nazewnictwo i konwencje projektu.
6. W kodzie C# i XAML unikaj niepotrzebnego zawijania linii.
7. W WPF stosuj MVVM. Nie dodawaj logiki aplikacyjnej do code-behind.
8. Zachowuj izolację danych tenantów oraz istniejące mechanizmy autoryzacji.
9. Nie modyfikuj mechanizmów szyfrowania i przechowywania poświadczeń bez osobnej zgody.
10. Nie wykonuj migracji bazy danych bez zgody użytkownika.
11. Po zmianach przedstaw listę zmodyfikowanych plików i wyniki dostępnych testów.
12. Nie wykonuj automatycznie commitów ani push do Git.

## Aktualny etap
Porządkowanie aplikacji WPF przed dodaniem filtrowania i sumowania faktur.
## Kontekst projektu
Przed analizą lub modyfikacją projektu przeczytaj docs/XKSEF_CONTEXT.md.