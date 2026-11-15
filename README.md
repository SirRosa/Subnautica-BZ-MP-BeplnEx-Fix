# Subnautica Below Zero Multiplayer 3.0.1 — native plugin ports

Repozytorium eksperymentalnych wtyczek do multiplayera BOT Bensona **bez BepInEx i Nautilus**.

## Potwierdzone

- Użytkownik potwierdził wykonanie `Subnautica.BZ.MP.PortProbe.OnEnabled()` w 3.0.1: `[Sirro.MP.PortProbe] enabled`. Loader obsługuje lokalną DLL nawet gdy nie widać jej na liście launchera.
- Oryginalny kod i statyczne zależności trzech modów przeanalizowano. Szczegóły: [RESEARCH.md](RESEARCH.md).
- Build GitHub Actions kompiluje wtyczkę względem minimalnego kontraktu `Subnautica.API`, bez publikowania bibliotek gry.

## Etap 1 — Native SnapBuilder 0.1 (**eksperymentalny, nie pełny port**)

`src/NativeSnapBuilder`: osobna wtyczka `Sirro.BZ.MP.NativeSnapBuilder.dll`.

- Próbuje włączyć Harmony Postfix dla `Builder.SetPlaceOnSurface`.
- Przyciąga do siatki na płaszczyźnie powierzchni w lokalnych współrzędnych trafionego obiektu; pomija całe części baz (`ConstructableBase`).
- Siatka domyślnie `0.5` m, przy przytrzymaniu lewego/prawego Control `0.1` m; kliknięcie środkowym przyciskiem myszy (`Mouse2`) przełącza funkcję.
- Konfiguracja: `%APPDATA%\\.botbenson\\Subnautica Below Zero\\Game\\Config\\Sirro.NativeSnapBuilder.ini`.
- Diagnostyka: wpisy `[Sirro.NativeSnapBuilder] ENABLED` lub `DISABLED: ...` w logu multiplayera.

**Ograniczenia:** nie ma jeszcze obrotu co 45°/5°, poprawionych kolizji, suwaków zasięgu ani UI. Refleksyjne podłączenie Harmony i zachowanie w czasie rzeczywistej gry **nie są jeszcze potwierdzone**. GitHub Actions weryfikuje kompilację i samą matematykę siatki — nie uruchamia Subnautica Below Zero.

**Bezpieczny test:** sprawdź osobną, zapasową kopię zapisu. Nie testuj konstrukcji na wspólnym zapisie produkcyjnym, dopóki oba klienty nie będą poprawnie widzieć tej samej pozycji, a stan nie będzie przetrwał ponownego dołączenia. Usuń DLL, jeśli wystąpią błędy synchronizacji.

## Pozostałe porty

- Building Tweaks: funkcje przebudowane bez Nautilus; szczególnie ostrożnie z modyfikacjami hierarchii obiektów i walidacji umieszczania.
- Craft From Containers: zasoby ze skrzyń muszą być zużywane autorytatywnie po stronie hosta, aby nie dochodziło do duplikacji.

Oryginalne DLL i biblioteki komercyjnej gry nie są umieszczane w repozytorium.

Źródło inspiracji i porównania zachowania SnapBuilder: https://github.com/toebeann/SnapBuilder (LGPL-3.0).
