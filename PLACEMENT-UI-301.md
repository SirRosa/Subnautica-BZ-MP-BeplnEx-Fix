# Construtor Zero 0.3.3-rc4 — podłogi, CFC UI i wydajność

**Status: nowy kandydat do testów w BOT Benson Below Zero MP 3.0.1.** Testy GitHub Actions badają symulację Unity/Harmony i zgodność kontraktów. **Nie zastępują prawdziwej gry z dwoma klientami.** Przed testami zrób kopię serwera. Nie używaj tego wydania na jedynym zapisie świata.

## Problemy zgłoszone po rc3

1. Locker i podobne meble potrafiły wisieć nad podłogą, mimo że siatka X/Z działała.
2. Po wybraniu receptury z CFC menu mogło zatrzymać się na około sekundę.
3. Materiały widoczne w szafkach były czerwone w UI, chociaż po transferze można było craftować.
4. Każdy krok transferu był ogłaszany przez `ErrorMessage.AddMessage`, zasłaniając ekran.

Tryb clippingu mebli na `G` został przez gracza wstępnie potwierdzony jako działający; nie przebudowano jego sieciowej ścieżki.

## Poprawki

### 1. Snap do rzeczywistej podłogi
- Na trafieniu w prawie poziomą powierzchnię (`RaycastHit.normal.y >= 0.985`) modyfikacja odczytuje `RaycastHit.point` oraz najniższy narożnik `Builder.bounds.GetCorners()`.
- Pozycja podstawy mebla jest wyznaczana z punktu fizycznego kontaktu i dolnej granicy jego bryły. Sam snap X/Z pozostaje bez zmian (Ctrl 0,1 m, ŚPM toggle).
- Gdy game-API nie udostępnia poprawnych narożników albo trafienie dotyczy ściany/sufitu, plugin pozostawia niepewną wysokość **bez zmian** — nie przesuwa konstrukcji na ślepo.
- W multiplayerze domyślne rozszerzenie powierzchni mebli z Building Tweaks jest wyłączone, **chyba że gracz włączy `G`**. Zapobiega przypisywaniu meblom podłogowym powierzchni ścian i sufitów jako normalnych punktów stawiania.
- `ground_align=true` w `ConstrutorZero.ini` (domyślnie). Jeśli jakiś prefab mimo tego ustawia się źle, można ustawić `ground_align=false` i porównać działanie.
- To nie jest port algorytmu polepszania colliderów z oryginalnego SnapBuildera. Duże różnice colliderów/mieszczeń wymagają osobnego testu; nie gwarantujemy kontaktu z każdą powierzchnią.

### 2. CFC bez sekundowych pauz przy podglądzie
- Zamiast skanować wszystkie schowki dla każdego typu składnika z osobna i ponawiać to co klatkę, wtyczka buduje **jeden wspólny obraz zawartości wszystkich typów** co najwyżej raz na 850 ms.
- Zwykłe podglądanie receptur nie przeszukuje tysięcy indywidualnych identyfikatorów sieciowych. Dokładny `ItemId` nadal jest sprawdzany **przed rzeczywistym transferem** i po nadejściu ACK z serwera.
- Skanowanie świata przez `FindObjectsOfType` odbywa się dużo rzadziej. Nowo tworzone szafki są nadal rejestrowane przez hook `StorageContainer.Awake`.
- Przy właściwym **kliknięciu budowania** cache zostaje odświeżony: stary podgląd nie może wygenerować pakietu budowania bez prawdziwych materiałów.
- Podgląd może być nieaktualny przez maksymalnie 850 ms po zmianie zawartości szafki; jest wyłącznie informacją. Serwer zachowuje autorytet.

### 3. Czytelne kolory materiałów
- `uGUI_RecipeEntry.UpdateIngredients`: przelicza wyświetlane ilości jako ekwipunek + dostępne szafki (tylko warstwa UI).
- `TooltipFactory.CraftRecipe`: etykiety składników fabricatora są zielone, jeśli łączna lokalnie widoczna liczba wystarczy, czerwone w przeciwnym przypadku.
- **Nie podmienia `Inventory.GetPickupCount` ani `Inventory.DestroyItem`**. Crafting nadal uruchamia się dopiero po realnym serwerowym transferze itemów do ekwipunku.

### 4. Cichy HUD
- Domyślne `show_cfc_messages=false` wyłącza `ErrorMessage.AddMessage` dla rutynowych transferów i potwierdzeń.
- Informacje diagnostyczne nadal trafiają do logu klienta.
- Dla świadomie włączonych komunikatów dodaj `show_cfc_messages=true` do `ConstrutorZero.Containers.ini`. Aktualizacja DLL nie nadpisze już istniejących plików ini.

## Instalacja

1. Wyłącz grę obu klientów; skopiuj pliki świata serwera i konfiguracje w bezpieczne miejsce.
2. Usuń starszą DLL Construtor Zero i NativeSnapBuilder, zostaw tylko tę samą wersję `Sirro.BZ.MP.ConstrutorZero.dll` na obu klientach:
   `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`
3. Konfiguracja:
   `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\`.
   - `ConstrutorZero.ini`: `enabled=true`, `ground_align=true`.
   - `ConstrutorZero.Containers.ini`: `enabled=true`, `auto_craft_enabled=true`, `show_cfc_messages=false`.
   - Dla bezpiecznego początku: `auto_build_enabled=false`, `auto_place_after_prefetch=false`.
   - `ConstrutorZero.Building.ini`: `allow_multiplayer_furniture_clipping=true`; `G` nadal przełącza ten tryb w trakcie budowania.
4. Zrestartuj grę po zapisaniu ini.

## Minimalny test kontrolowany

- [ ] Ustaw zwykły Locker na płaskiej podłodze (bez G): podstawa styka się z podłogą. Sprawdź osobno przy wyłączonym snappingu i po jego włączeniu ŚPM.
- [ ] Sprawdź Locker i 2–3 inne meble w różnych pomieszczeniach, na gruncie oraz na pochyłości. Nie mogą wisieć ani zanurzać się w powierzchni.
- [ ] Z `G` przesuń szafkę przez inną szafkę i przy ścianie; drugi gracz musi widzieć tę samą pozycję. Moduły bazowe nie mogą zyskać nieautoryzowanego clippingu.
- [ ] Na bazie pełnej schowków nawiguj szybko po 20 recepturach. Zanotuj ewentualne zatrzymania UI (CI nie mierzy FPS w grze).
- [ ] Wybierz recepturę, której składniki są tylko w zwykłym Locker/SmallLocker: tooltip i przypięty przepis powinny pokazywać zieloną dostępność; natomiast prawdziwy ekwipunek pozostaje niezmieniony aż do serwerowego transferu.
- [ ] Wykonaj crafting i sprawdź ekwipunek, stan schowka i powtórne dołączenie. Nie może dojść do duplikacji/utraty.
- [ ] Na ekranie nie powinny pojawiać się statusy każdego transferu CFC, a log powinien nadal zawierać diagnostykę.
- [ ] Powtórz testy przy obu klientach, po reconnect i po restarcie serwera.

## Ograniczenia wciąż nieusunięte

- Brak atomowej transakcji serwerowej wielu surowców oraz rollbacku całej receptury.
- Tylko `Locker` i `SmallLocker` mają potwierdzoną w kodzie ścieżkę transferu; inne pojemniki wymagają oddzielnego portu.
- Brak pełnego oryginalnego algorytmu SnapBuilder (m.in. korekcja złożonych colliderów i wall-to-floor pop) oraz pełnego Building Tweaks.
- Brak certyfikacji z rzeczywistych dwóch klientów. Wszystkie testy CI są kontraktowe albo symulacyjne.

**W razie uniesionej/zagłębionej konstrukcji, duplikacji, odmiennych pozycji na klientach lub błędu transferu natychmiast przerwij i zachowaj log obu klientów oraz serwera.**
