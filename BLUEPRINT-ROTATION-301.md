# Construtor Zero 0.3.6-rc7 — blueprinty bez składników, donice ścienne, pionowe meble

**STATUS: kandydat do testu multiplayer, nie potwierdzone wydanie stabilne.** Automatyczne testy w CI sprawdzają kontrakt BZ i zasymulowane Harmony/BOT Benson, ale nie zastępują prawdziwej gry dwóch osób na serwerze. Testuj na KOPII zapisu.

## 1. Naprawa: wolne blueprinty nawet bez ani jednego surowca

Błąd rc6: `AutoBuildCoordinator.Prefix` na `Builder.TryPlace` zwracał `false`, gdy brakowało materiałów w plecaku oraz szafkach, blokując NORMALNY pakiet stawiania blueprintu. Co gorsza, po ACK z serwera kod potrafił ponownie wywołać `Builder.TryPlace`. To mieszało postawienie niezbudowanego obiektu z jego późniejszą konstrukcją.

**Zmiana: `Builder.TryPlace` nigdy nie jest blokowany ani odtwarzany przez CFC.** Zatwierdzenie blueprintu nadal pozostaje po stronie oryginalnej gry/BOT Benson. Przy `auto_build_enabled=true`, dostępnych *załadowanych* zasobach i dozwolonej pozycji może niezależnie rozpocząć się pobieranie składników do rzeczywistego ekwipunku poprzez serwer. Gdy surowców brak — blueprint nadal można postawić. Dalsze fizyczne konstruowanie odbywa się oryginalnym narzędziem budowania. Opcja `auto_place_after_prefetch` nie doprowadzi do ponownego postawienia blueprintu.

Zachowane: sprawdzanie ItemId, ACK, kontrola TechType, brak duplikacji żądań i brak ingerencji w oryginalne pakiety budowania.

## 2. Donice i inne obiekty ścienne z regulowaną wysokością

Błąd rc6: poziomy promień `Physics.RaycastAll` zaczynał zawsze z `MainCamera.position.y`, więc przesuwanie celownika w górę i w dół nie zmieniało miejsca kontaktu ze ścianą.

**Zmiana:** pionowy celownik zmienia teraz wysokość POCZĄTKU poziomego promienia, w granicach do 1,5 m względem kamery i rzeczywistego zasięgu Buildera. Promień nadal wybiera najpierw konstrukcyjną ścianę za meblami, z pozostawieniem opcjonalnego snapowania do samych mebli, gdy oryginalne `allowedOnConstructables` na to pozwala. Wysokość jest zaokrąglana do globalnej siatki 0,5 m / Ctrl 0,1 m — nie przypięta na stałe do wzroku. Obsługiwane są również `TechType.PlanterShelf`, `SmallLocker`, `PictureFrame`, `SingleWallShelf` i pozostałe prefabrykaty z natywną listą powierzchni zawierającą wyłącznie Wall.

Kiedy wycelowany punkt wypada poza legalnym zasięgiem konstrukcji, system próbuje kilka niższych/wyższych wysokości w małych krokach zamiast wymuszać niepoprawne miejsce.

## 3. Łóżka, Lockery i meble nie rotują w pionie

Błąd rc6: `Builder.SetPlaceOnSurface` mógł wytworzyć niepoprawny `Quaternion.LookRotation` przy bardzo stromym promieniu z kamery lub nierównej normalnej. Poprzedni patch zostawiał tę rotację, więc łóżka potrafiły stanąć pionowo.

**Zmiana:** po otrzymaniu legalnego floor/wall hit tworzymy osobno **uśrednioną w płaszczyźnie poziomej orientację Yaw, bez Pitch i Roll**. Floor: stabilny poziomy kierunek kamery (z zapamiętanym yaw dla niemal pionowego spojrzenia); Wall: pozioma normalna trafionej ściany. Respektowane są `rotationEnabled`, oryginalne `Builder.additiveRotation`, standardowa siatka rotacji oraz Alt z precyzyjnym krokiem. Natywne `Bed1`, `Bed2`, `NarrowBed` (oraz nazwy rozpoczynające się od Bed) są klasyfikowane jako obiekty podłogowe nawet jeśli prefab omyłkowo dopuszcza kilka powierzchni.

Pozycja i rotacja przechodzą przez oryginalną walidację przestrzeni, przed pakietem multiplayer. Modułów bazy nie modyfikujemy.

## 4. Craft From Containers: 100 m — rzeczywisty stan

W pliku `ConstrutorZero.Containers.ini` domyślnie **`range_meters=100`** (dozwolone do 300 m). Test kontraktowy potwierdza, że **załadowana szafka w odległości 90 m jest liczona, a 110 m nie**.

Ale klientowy skan `StorageContainer` widzi **tylko obiekty aktualnie załadowane w scenie**. BOT Benson do przesłania pojedynczego przedmiotu używa `StorageItemRemovingEventArgs`, a jego procesor potrzebuje rzeczywistego obiektu `Pickupable` do `WorldPickupItem.Create`. Jeśli szafka jest odstreamowana, ustawienie 100 m nie pomoże. Gwarantowane 100 m niezależnie od streamingu będzie wymagało obsługi identyfikacji i żądania przedmiotu po stronie **serwera** oraz nowego protokołu lub bezpiecznego rozszerzenia istniejącego; nie zostało zaimplementowane w tej klientowej DLL.

Na próbie blueprintu, gdy żaden komplet materiałów nie jest dostępny, log zawiera teraz jednorazowy `CFC_BLUEPRINT_SOURCE_NOT_FOUND` z `config_range_m`, `loaded_storage`, `loaded_in_radius`. Dzięki temu można oddzielić problem ustawienia od odstreamowanych kontenerów.

## Konfiguracja

DLL ze spakowanego ZIP (oryginalna nazwa `Sirro.BZ.MP.ConstrutorZero.dll`) zainstaluj **na obu klientach** w:

`%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`

Konfiguracja w `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\`:

`ConstrutorZero.ini`: `enabled=true`, `ground_align=true`.

`ConstrutorZero.Containers.ini`: `enabled=true`, `range_meters=100`, `auto_craft_enabled=true`, `auto_build_enabled=true` (włącz dopiero po pierwszych testach), `show_cfc_messages=false`, `auto_place_after_prefetch=false`.

**Stare pliki ini nie są nadpisywane przy aktualizacji DLL.** Ustaw rzeczywiste wartości, zrestartuj obu klientów i sprawdź `CFC_READY` w logu.

## Testy akceptacyjne

1. Bez składników w plecaku i żadnej szafce: postaw niezbudowany blueprint (np. łóżko/Locker) w prawidłowej pozycji; drugi gracz powinien go widzieć. NIE powinien od razu stać się ukończonym obiektem. Następnie zbuduj go standardowym narzędziem.
2. Z materiałami tylko w załadowanej szafce: postaw blueprint — musi pojawić się dokładnie JEDEN pakiet budowania. Składniki mogą zostać dostarczone później do plecaka; nie może dojść do drugiego, automatycznego stawiania.
3. Przemieszczaj celownik po ścianie w górę i dół z wybraną `PlanterShelf` albo `SmallLocker`; pozycja powinna zmieniać wysokość po globalnym gridzie.
4. Wybierz Bed1, Bed2, NarrowBed i Locker; patrz pod różnymi kątami. Wszystkie powinny być poziome na podłodze (bez pochylenia lub rotacji pionowej).
5. Sprawdź meble przy ścianie i G clipping; nie powinny uzyskać nielegalnego budowania w powietrzu ani pozwalać modyfikować modułów konstrukcyjnych.
6. Z materiałami w szafce w odległości 15 m, 30 m, 50 m i 90 m porównaj `CFC_READY`, `CFC_STORAGE_SCAN` i `CFC_BLUEPRINT_SOURCE_NOT_FOUND`. Jeśli `loaded_in_radius=0`, szafka nie jest widoczna dla klienta i sam zakres 100 m niczego nie zagwarantuje.
7. Zweryfikuj brak desynchronizacji, duplikacji oraz utraty przedmiotów przy dwóch graczach, po ponownym dołączeniu i restarcie serwera.

## Ograniczenia

- **Nie gwarantuje zasięgu CFC 100 m przy szafkach niezrenderowanych/zdespawnowanych przez grę**; w tym zakresie konieczna będzie oddzielna obsługa serwerowa.
- Ograniczony port Building Tweaks i SnapBuilder, brak pełnej synchronizacji Attach to Target oraz pełnego algorytmu 3D colliderów.
- CFC nie zapewnia atomowej transakcji wieloskładnikowej ani rollbacku po przerwaniu craftingu.
- Tylko `Locker` i `SmallLocker` mają obecnie potwierdzoną ścieżkę zdalnego transferu w tym modzie.
- Zielone CI nie jest potwierdzeniem działania w grze. Testuj na kopii świata, nie na jedynym zapisie.
