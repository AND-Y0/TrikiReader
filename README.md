# TrikiReader

Triki od Żabki to mały kontroler do gier mobilnych, który wygląda jak kapsel i działa z aplikacją Żappka. Służy do sterowania grami ruchem, a za wyniki można zdobywać żappsy, zniżki i inne nagrody powiązane z ekosystemem Żabki. 

W środku Triki posiada sensory, takie jak żyroskop i akcelerometr, dzięki czemu precyzyjnie wykrywa poruszanie, obracanie czy podrzucanie urządzenia. Zazwyczaj łączy się ze smartfonem i pozwala grać w gry mobilne dostępne w Strefie Gier w Żappce.

**TrikiReader** to otwarta aplikacja WPF dla środowiska Windows, służąca do testowania tego kontrolera, odczytywania danych z wbudowanego IMU (przez Bluetooth Low Energy) i sprzętowej wizualizacji orientacji 3D kapsla w czasie rzeczywistym.

## Co robi aplikacja

- wyszukuje urządzenie BLE o nazwie zawierającej `Triki`,
- łączy się z Nordic UART Service,
- subskrybuje powiadomienia z charakterystyki TX,
- dekoduje 14-bajtowe ramki ze zintegrowanego czujnika IMU,
- stosuje fuzję danych z żyroskopu i akcelerometru przy pomocy filtru **Madgwick AHRS**,
- wizualizuje ułożenie kapsla w przestrzeni za pomocą modelu 3D w czasie rzeczywistym,
- rysuje płynny wykres aktywności z trzech osi żyroskopu (X, Y, Z).

## Specyfikacja BLE

Urządzenie korzysta z Nordic UART Service do komunikacji:

- Service UUID: `6e400001-b5a3-f393-e0a9-e50e24dcca9e`
- RX Characteristic: `6e400002-b5a3-f393-e0a9-e50e24dcca9e`
- TX Characteristic: `6e400003-b5a3-f393-e0a9-e50e24dcca9e`

Aplikacja subskrybuje powiadomienia z TX i wysyła komendę inicjującą przesył danych do RX:

```text
20 10 00 D0 07 68 00 03
```

## Format danych

Dane IMU przychodzą jako ramki 14-bajtowe z nagłówkiem:

```text
22 00
```

Układ ramki:

```text
22 00 | gyroX | gyroY | gyroZ | accelX | accelY | accelZ
```

Każda oś przesyłana jest jako 16-bitowa liczba całkowita ze znakiem w formacie little-endian. Oprogramowanie wykorzystuje domyślne przeliczniki skalowania sprzętowego: `131.0` dla żyroskopu i `2048.0` dla akcelerometru.

Analiza capture BLE i aplikacji Żappka `4.37.0` wskazuje, że strumień przychodzi burstami BLE, zwykle po kilka ramek w krótkim pakiecie. TrikiReader używa timestampów powiadomień BLE jako źródła czasu próbek, bo eksperymentalny stały zegar około `104 Hz` powodował gorszą płynność w praktycznym teście.

## Uruchomienie

Wymagania:

- System Windows z włączonym modułem Bluetooth,
- Zainstalowany .NET 9 SDK,
- Włączone urządzenie Triki w pobliżu.

Kompilacja:

```powershell
dotnet build
```

Uruchomienie:

```powershell
dotnet run --project TrikiReader.csproj
```

W interfejsie aplikacji kliknij `Połącz z Triki`. Jeśli urządzenie było w stanie uśpienia, kliknij przycisk na kapslu tuż przed nawiązaniem połączenia, aby je wybudzić.

## Testy

Projekt testowy `TrikiReader.Tests` weryfikuje poprawność parsera ramek, konwersję jednostek IMU, odrzucanie początkowego "szumu" przy uruchamianiu urządzenia oraz zbieżność i precyzję zaimplementowanego filtru orientacji wizualnej.

```powershell
dotnet test
```
