# Construtor Zero 0.3.4-rc5 — rzeczywiste mocowanie do powierzchni

**Status: kandydat do kolejnego testu w grze, a NIE potwierdzone stabilne wydanie.** Oryginalny Builder Below Zero i synchronizacja BOT Benson MP 3.0.1 nadal wymagają testu na dwóch klientach i kopii świata.

## Dlaczego Locker nadal wisiał nad podłogą

W rc4 port wywoływał `OrientedBounds.GetCorners()` przez refleksję. W oryginalnym Tobey SnapBuilder jest to **metoda rozszerzająca dostarczana przez SnapBuilder**, nie instancyjna metoda `OrientedBounds` w grze. Fake do CI błędnie ją implementował i ukrywał problem. Odtąd dolny narożnik wyliczany jest ze wspieranych przez grę pól `OrientedBounds.position` i `OrientedBounds.extents`, tak jak robi to oryginalne rozszerzenie.

Drugą przyczyną był sam kierunek celowania kamery: bazowy `Builder` oblicza domyślną pozycję ghosta względem `camera.forward` i sprawdza trafioną powierzchnię **dopiero później**. Poprawianie samej wysokości w `SetPlaceOnSurface` nie usuwało sytuacji, gdy celownik trafiał w ścianę lub w ogóle nie trafiał w podłogę.

## Nowy Surface Attachment Bridge

- Harmony postfix `Builder.CheckAsSubModule` działa **wewnątrz** oryginalnego procesu sprawdzania pozycji, przed końcowymi kontrolami kolizji i przed pakietem budowania w multiplayerze.
- Dla mebli wymagających wyłącznie podłogi (szczególnie `TechType.Locker`) wysyła pomocniczy promień fizyki **pionowo w dół**, w punkcie przed graczem wyznaczonym względem poziomego kierunku kamery, a nie jej pitch.
- Dla mebli wyłącznie ściennych (`TechType.SmallLocker`) szuka **ściany** promieniem poziomym, niezależnie od tego, czy gracz patrzy wysoko czy nisko.
- Dla pozostałych prefabów odczytuje listę `Constructable.allowedSurfaceTypes`; mebli obsługujących wiele typów powierzchni nie przypisuje arbitralnie do jednej z nich.
- Po trafieniu odtwarza legalną pozycję przez oryginalne `Builder.SetPlaceOnSurface` (z istniejącą siatką 0,5/0,1 m), a następnie przekazuje trafiony **Collider** przez parametr `out` oryginalnego `CheckAsSubModule`.
- Przed zaakceptowaniem pozycji sprawdza rodzaj powierzchni, `Constructable.CheckFlags`, tagi i minimalny/maksymalny zasięg, zakaz budowania na SeaTruck, dozwolony typ obiektu pod spodem, `ValidateOutdoor` i oryginalną `Builder.CheckSpace`.
- Jeśli nie ma dozwolonej powierzchni: **nie da się postawić obiektu**. Jeśli wcześniej była znana prawidłowa pozycja, ghost pozostaje tam wizualnie (czerwony, nieplaceable) aż do kolejnego poprawnego trafienia; nie podąża pionowo za kamerą.
- W multiplayerze `G` może ignorować przeszkody **tylko dla zwykłych mebli**, ale nie może już fałszywie zatwierdzać całej pozycji bez dozwolonej powierzchni ani zmieniać listy legalnych powierzchni w prefabie.
- Ograniczono ponowne wyszukiwanie Type przez refleksję: rozpoznane typy gry/Unity są cache'owane, brakujące typy nie.

## Instalacja

Zastąp starszy plugin **taką samą DLL na obu klientach** w folderze:
`%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\Sirro.BZ.MP.ConstrutorZero.dll`.
Nie kopiuj atrap DLL z CI. Pozostaw wcześniejszy config CFC (funkcje craftingu z rc4 są niezmienione). W `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\ConstrutorZero.ini` ustaw `ground_align=true`. Jeśli modyfikowałeś plik, nie zostanie on nadpisany przez aktualizację. Wykonaj kopię zapisu świata przed testem.

## Kluczowy test dwóch klientów

1. Na płaskiej podłodze w bazie wybierz **Locker**; poruszaj kamerą mocno w górę i w dół. Szafka powinna pozostawać w tej samej płaszczyźnie podłogi, zamiast poruszać się wraz z wysokością celownika.
2. Powtórz z włączonym i wyłączonym snapem ŚPM, z Ctrl 0,1 m i przy ścianie. Sprawdź, czy podstawa dotyka podłogi, nie wisi i nie zapada się.
3. Wybierz **SmallLocker**, kieruj kamerę wysoko i nisko; powinien szukać ściany, a nie podłogi.
4. Odsuń się od każdej legalnej powierzchni: obiekt nie może zostać postawiony w pustce. Gdy wcześniej dotykał podłogi, ghost może pozostać w starej lokalizacji, ale jego stawianie musi być zablokowane.
5. `G` ma nadal pozwalać nakładać meble, ale **nie** pozwalać stawiać Lockera bez podłogi lub modułu bazy w nieprawidłowym miejscu.
6. Sprawdź u drugiego gracza pozycję postawionego mebla przed i po wyjściu/wejściu oraz restarcie serwera. Przerwij test przy desynchronizacji.
7. Jeśli dalej nie działa, dołącz log klienta i wpisy `BUILDING_HOOK: Builder.CheckAsSubModule`, `SURFACE_LOCK_FIRST_CONTACT`, `SURFACE_LOCK_NO_RAY_HIT`, `SURFACE_LOCK_FAILED`; ułatwi to rozróżnienie braku hooka od problemu z raycastem.

## Limity

- Wersja nie gwarantuje idealnego mocowania do każdego nierównego/niestandardowego collidera; brak pełnego algorytmu szczegółowych colliderów i wall-to-floor pop z oryginalnego SnapBuilder.
- Nie ma pełnej obsługi obiektów dopuszczających kilka różnych typów powierzchni.
- Brak atomowych transferów serwerowych dla receptur wieloskładnikowych CFC pozostaje ograniczeniem BOT Benson.
- Zielone CI testuje wyłącznie kontrakt i symulację zachowania; nie jest dowodem działania w prawdziwym świecie gry.
