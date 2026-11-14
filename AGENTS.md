
# xKsef — instrukcje dla Codex

## Projekt
xKsef to aplikacja .NET 10 do integracji z Krajowym Systemem e-Faktur (KSeF).

Rozwiązanie zawiera aplikację WPF, ASP.NET Core Web API, warstwę Infrastructure, Domain oraz osobny CredentialService.

## Zasady współpracy
1. Nie pytaj o zgodę na standardowe operacje programistyczne, diagnostyczne i testowe.
2. Samodzielnie analizuj kod, wybieraj rozwiązania, implementuj je i uruchamiaj testy.
3. Jeśli wystąpi błąd, zdiagnozuj go, popraw i ponownie zweryfikuj rozwiązanie.
4. Nie zatrzymuj się po każdym kroku. Kontynuuj pracę aż do zakończenia zleconego zadania.
5. Możesz samodzielnie tworzyć i modyfikować pliki, klasy, testy, konfiguracje i migracje EF Core.
6. Możesz wykonywać kompilacje, testy, polecenia Git, operacje na lokalnych bazach developerskich oraz uruchamiać aplikacje.
7. Możesz korzystać z dokumentacji i Internetu, gdy jest to potrzebne.
8. Podejmuj samodzielnie typowe decyzje techniczne, zachowując istniejącą architekturę i standardy projektu.
9. Nie wprowadzaj niepowiązanych zmian ani nie rozszerzaj samodzielnie zakresu funkcjonalnego zadania.
10. Nie pytaj o zgodę na kolejne etapy realizacji tego samego zadania.
11. Nie zmieniaj architektury bez wyraźnej zgody.
12. Nie usuwaj istniejących metod, klas ani właściwości bez sprawdzenia ich użycia.
13. Zachowuj dotychczasowe nazewnictwo i konwencje projektu.
14. W kodzie C# i XAML unikaj niepotrzebnego zawijania linii.
15. W WPF stosuj MVVM. Nie dodawaj logiki aplikacyjnej do code-behind.
16. Zachowuj izolację danych tenantów oraz istniejące mechanizmy autoryzacji.
17. Nie modyfikuj mechanizmów szyfrowania i przechowywania poświadczeń bez osobnej zgody.
18. Po zmianach przedstaw listę zmodyfikowanych plików i wyniki dostępnych testów.
19. Nie wykonuj automatycznie commitów ani push do Git.

## Aktualny etap
Porządkowanie aplikacji WPF przed dodaniem filtrowania i sumowania faktur.
## Kontekst projektu
Przed analizą lub modyfikacją projektu przeczytaj docs/XKSEF_CONTEXT.md.