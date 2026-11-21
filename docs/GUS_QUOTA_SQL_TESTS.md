# Testy magazynu zgód GUS w SQL Server

Testy są jawnie uruchamiane. W zwykłym zestawie jednostkowym mają status pominiętych.
Nie wykonują zapytań HTTP ani operacji GUS.

Uruchomienie z katalogu rozwiązania:

```powershell
dotnet test xKsef.Tests/xKsef.Tests.csproj --settings xKsef.Tests/GusQuota.Sql.runsettings --filter Category=ManualGusQuotaSql
```

W Visual Studio wybierz `Test > Configure Run Settings > Select Solution Wide runsettings File`,
wskaż `xKsef.Tests/GusQuota.Sql.runsettings` i uruchom wyłącznie `GusQuotaSqlServerTests`.
Po zakończeniu odznacz ten plik ustawień, aby przywrócić domyślne pomijanie testów SQL.

Wymagany jest SQL Server LocalDB `(LocalDb)\MSSQLLocalDB` i prawo tworzenia baz na tej instancji.
Fixture sprawdza `SERVERPROPERTY('IsLocalDB')`, tworzy bazę `xKsef_Tests_<losowy GUID>`,
stosuje wszystkie migracje i usuwa wyłącznie własną bazę po zakończeniu.
Nie odczytuje połączenia aplikacji ani poświadczeń z jej konfiguracji.

Testy weryfikują wszystkie trzy okna, czas następnej próby, niezależność środowisk,
anulowanie, timeout SQL, nieuzyskanie blokady, niedostępną bazę oraz równoległe próby
przez 48 niezależnych połączeń. Timeout SQL jest sprawdzany przez celową blokadę tabeli
wyłącznie w bazie testowej. Przesunięcie historii w testach minuty/godziny zastępuje
czekanie przez minutę/godzinę; produkcyjny zegar SQL nie jest zastępowany.

Magazyn wykonuje tylko jedną próbę, bez ponowień. Czas ważności lokalnej zgody wynosi
250 ms; konserwatywnie odliczany jest cały czas próby SQL. Odpowiedź może zawierać
już przeterminowaną zgodę. W przyszłej integracji wysyłający musi sprawdzić `TryUse()`.
Ani wygaśnięcie, ani błąd czy anulowanie po commit nie zwracają zgody do puli.
