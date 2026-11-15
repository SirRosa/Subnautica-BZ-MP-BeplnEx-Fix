# Construtor Zero — połączony SnapBuilder + Building Tweaks, BZ multiplayer 3.0.1

**Status: 0.1-alpha, eksperymentalny port funkcji, NIE pełna kompatybilność.**

Cel: jedna natywna DLL `Sirro.BZ.MP.ConstrutorZero.dll`, bez BepInEx, Nautilus i modyfikowania oficjalnych DLL multiplayera.

## Potwierdzone i ograniczenia testu

- Użytkownik potwierdził wcześniej działanie metody `OnEnabled` dla niezależnego natywnego `PortProbe` w wersji 3.0.1.
- GitHub Actions buduje bibliotekę i testuje publiczny kontrakt API, matematykę siatki, kąty oraz bezpieczny stan opcji.
- GitHub Actions **nie uruchamia gry** i nie testuje synchronizacji dwóch klientów.
- Oryginalna wersja multiplayera BOT Bensona różni się od publicznego repo, dlatego źródło publiczne służy do zrozumienia protokołu, a nie za źródło prawdy o 3.0.1.

## Implementacja 0.1-alpha

| Funkcja oryginału | Aktualna implementacja | Status / ryzyko |
|---|---|---|
| SnapBuilder: podstawowy snap pozycji | Lokalna siatka na płaszczyźnie trafionej powierzchni, względem transform obiektu; domyślnie 0.5 m | Do testu w grze |
| SnapBuilder: fine snap | Ctrl: 0.1 m | Do testu |
| SnapBuilder: fine rotation | Yaw co 45°, Alt co 5°, **tylko płaskie podłoże i elementy z rotationEnabled** | Uproszczenie wobec oryginału, do testu |
| SnapBuilder: zasięg budowania | B włącza zakres x2; Mouse4/Mouse3 reguluje x1–x10; zapamiętuje i przywraca odległości prefabu | Do testu, brak odpowiednika patcha Targeting.GetTarget |
| SnapBuilder: zaawansowane raycast/snap AimTransform | Nieprzeniesione | Brak pełnej zgodności |
| SnapBuilder: ulepszanie colliderów i podgląd | Nieprzeniesione | Brak pełnej zgodności |
| SnapBuilder: PlaceTool rotation, UI hints | Nieprzeniesione | Brak pełnej zgodności |
| Building Tweaks: rozszerzone dozwolone powierzchnie | Rozszerzenie flag dla obiektów niebędących głównymi modułami baz | Do testu |
| Building Tweaks: blokada niszczenia chronionych obiektów | Filtr prefabów z komponentami Creature / BaseGhost / Constructable / LifepodDrop i blacklistą nazw | Do testu |
| Building Tweaks: naprawa PowerConsumer | Odczyt z powerRelay, gdy brak baseComp | Do testu |
| Building Tweaks: free placement | KeypadDivide przełącza; Keypad4/6, 2/8, +/- przesuwają pozycję ghosta | Do testu; obecnie w globalnych osiach, inaczej niż oryginał |
| Building Tweaks: Full Override | Płatki walidacji, obstacles, deconstruction z bramką `allow_unsafe_world_changes` oraz bezwzględną blokadą przy aktywnym multiplayerze | **WYŁĄCZONE domyślnie**: może desynchronizować |
| Building Tweaks: Attach to Target (mobiles/creatures/vehicles) | Wykrywa polecenie, ale **nie wykonuje parenting** | **NIEDOSTĘPNE**: brak synchronizacji hierarchii |
| Building Tweaks: pillars, Base.Start/Update, BaseGhost.Finish, player rescue | Nieprzeniesione | Wymaga odrębnej implementacji i testów |
| Pozostałe efekty typu collider/raycast/move parent | Nieprzeniesione | Brak pełnej zgodności |

**WAŻNE:** nie włączaj `allow_unsafe_world_changes=true` na współdzielonym zapisie. Flaga `allow_unsafe_world_changes=true` **nie odblokowuje** funkcji podczas aktywnej sesji multiplayer. Zmiana jest blokowana w kodzie, niezależnie od konfiguracji.

## Instalacja na kopii zapisu

1. Wykonaj kopię zapasową zapisów hosta.
2. Usuń z `Game/Plugins` poprzednią wtyczkę `Sirro.BZ.MP.NativeSnapBuilder.dll`, a także `Subnautica.BZ.MP.PortProbe.dll` (test nie jest już potrzebny).
3. Z paczki testowej skopiuj **tylko** `Sirro.BZ.MP.ConstrutorZero.dll` do `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`.
4. Uruchom launcher multiplayera 3.0.1. Skonfiguruj na obu klientach ten sam zakres i funkcje; obaj gracze powinni używać tej samej DLL.
5. W logu `Game/Logs` szukaj `[ConstrutorZero] ENABLED`, `BUILDING_PATCHES_INSTALLED`, `BUILDING_HOOK_FAILED` i `POSITION_CHANGED`.

## Sterowanie

| Skrót | Działanie |
|---|---|
| Środkowy przycisk myszy (Mouse2) | Snap ON/OFF |
| Ctrl | Precyzyjna siatka 0.1 m |
| Alt | Dokładna rotacja po 5° (dla dozwolonych rotacji) |
| B | Rozszerzony zasięg budowania |
| Mouse4 / Mouse3 | Zwiększanie / zmniejszanie rozszerzonego zasięgu |
| KeypadDivide | Ręczne pozycjonowanie (free placement) |
| Keypad4/6, 2/8, +/- | Przesuwanie pozycji podglądu |
| G | Full Override — zablokowane domyślnie dla ochrony synchronizacji |
| T | Attach to Target — zgłoszenie intencji, bez zmiany parenta w MP |

## Konfiguracja

Powstają dwa pliki lokalne (brak Nautilus i jego okna ustawień):

- `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\ConstrutorZero.ini`: siatka, kąty obrotu, stan snap.
- `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\ConstrutorZero.Building.ini`: powierzchnie, power check, ręczne przesuwanie, bramka niebezpiecznych operacji.

## Dlaczego nie ma jeszcze pełnego Attach to Target?

Na podstawie publicznych źródeł multiplayera `ConstructionGhostTryPlacingArgs` przesyła m.in. `Position`, `Rotation`, `SubrootId` i `BaseGhostComponent`. Nie przesyła uniwersalnego identyfikatora dowolnego parent obiektu (pojazd, creature, pickupable), do którego oryginalny Building Tweaks potrafi podpiąć konstrukcję. Samo lokalne `transform.SetParent` kończyłoby się inną hierarchią u pozostałych graczy.

Pełne wsparcie wymaga **osobnego klient-serwer protokołu parentowania, walidacji identyfikatorów i serializacji do zapisu**, a także potwierdzenia, jak loader 3.0.1 przyjmuje własne pakiety. Nie wprowadzaj niesprawdzonych rodziców obiektów na produkcyjny zapis.

## Źródła

- SnapBuilder (LGPL-3.0): https://github.com/toebeann/SnapBuilder
- BuildingTweaks: https://github.com/MrPurple6411/My-Subnautica-Mods/tree/main/BuildingTweaks
- Publiczny multiplayer (uwaga: może być nieaktualny): https://github.com/ismail0234/Subnautica-Below-Zero-Multiplayer

Nie kopiujemy zamkniętych DLL BOT Bensona do repozytorium. Jest to nowa implementacja eksperymentalna, a nie oficjalny port tych projektów.

## Automatyczna weryfikacja (2026-11-15)

GitHub Actions uruchamia `src/IntegrationHarness`, czyli symulowany runtime z Harmony 2.3.3 (wersja referencyjna z oficjalnych wtyczek multiplayera). Testuje:
- aktywację natywnego `SubnauticaPlugin`, `Harmony.Patch`, instalację patchy `Builder`, zasilania i walidacji;
- pozycjonowanie siatki `0.5 m`, `0.1 m`, obrót `45°`/`5°` (w tym ujemne kąty);
- rozbudowanie i przywrócenie zasięgów budowania (również prefabu);
- manualne przesunięcie ghosta, zatrzymanie i przywrócenie fabrycznego pozycjonowania;
- zabezpieczenie dekonstrukcji/kolizji/Full Override przy aktywnym multiplayerze;
- odpinanie patchy tylko tej wtyczki zgodne z Harmony 2.3.3 (`UnpatchAll(pluginId)`).

CI rozwiązało konkretny problem z `UnpatchSelf` — nie występuje w Harmony 2.3.3, podczas gdy nowsze HarmonyX taką metodę obsługują.

**Czego te testy NIE dowodzą:** zgodności wywołań `Assembly-CSharp` w rzeczywistej Subnautica BZ; aktywacji wszystkich poszczególnych hooków wobec właściwej gry; synchronizacji przy dwóch klientach; poprawności save po ponownym dołączeniu lub restarcie; pełnej zgodności z oryginalnymi modami.

Przy tym zakresie zewnętrznych zależności **nie należy oznaczać tego projektu jako „pełna wersja multiplayer 3.0.1”** i nie należy scalać go do `main` bez prawdziwych testów z grą.

Raport CI: https://github.com/SirRosa/Subnautica-BZ-MP-BeplnEx-Fix/actions
