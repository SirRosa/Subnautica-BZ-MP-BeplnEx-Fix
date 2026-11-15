# Construtor Zero 0.3.2-rc3 — poprawki po testach w prawdziwej grze

**Stan: kandydat do ponownego testu, NIE certyfikowane stabilne wydanie.**
Zgłoszone problemy z 0.3.1-rc2: CFC nie widziało zasobów w szafkach, siatka umieszczała przedmioty nad/pod powierzchnią, `G` nie pozwalało na clipping. Jedyną funkcją potwierdzoną przez gracza w tej wersji była poprawna synchronizacja zwykłego budowania. Poprawki poniżej mają zielone testy ABI/symulacyjne; wymagają jeszcze potwierdzenia w grze i na serwerze BOT Benson MP 3.0.1.

## Zmiany

### Craft From Containers
- Skanuje `UnityEngine.Object.FindObjectsOfType(StorageContainer)` po wejściu do świata oraz cyklicznie, zamiast polegać wyłącznie na zdarzeniu `StorageContainer.Awake`. Wcześniej postawione szafki mogły nie trafić do rejestru.
- Rozpoznaje konstrukcję magazynu na podstawie `ItemsContainer.tr.parent.gameObject` tak, jak multiplayerowy `ItemContainer.GetInformation`. Poprzednie sprawdzanie tylko `storageRoot.gameObject` mogło wskazywać niewłaściwe dziecko prefabrykatu.
- Używa wprost `ItemsContainer.GetItems(TechType, List<InventoryItem>)` do pobierania przedmiotów danego typu. Rzeczywisty `ItemsContainer` **również implementuje `IEnumerable<InventoryItem>`** — sam brak enumeracji nie był przyczyną; wcześniejsza diagnoza została poprawiona po zbadaniu DLL gry.
- Log `CFC_STORAGE_SCAN` podaje liczbę obiektów magazynowych w scenie, zarejestrowanych magazynów i **`supported_lockers`**. Serwerowa ścieżka transferu obsługuje obecnie **Locker i SmallLocker**.
- `CFC_SETTINGS` pokazuje wartości opcji. Jeśli wszystkie opcje CFC są wyłączone, w grze wyświetlany jest jednorazowy komunikat. Poprzednia konfiguracja mogła pozostawić `auto_craft_enabled=false` po aktualizacji.
- Każde przeniesienie nadal jest autoryzowane przez serwer; mod nie zwiększa sztucznie liczników ekwipunku i nie usuwa lokalnie zawartości szafek.

### Grid / Snap
- Przyciąganie działa na płaszczyźnie stycznej do powierzchni trafienia, zamiast niezależnie zaokrąglać X/Y/Z.
- Na płaskim podłożu Y nie jest zaokrąglane, więc przedmiot nie powinien skakać ponad/pod podłoże co 0,5 m.
- Na pochyłym terenie przesunięcie jest rzutowane na lokalną płaszczyznę styczną. Przy bardzo nierównej geometrii jest to przybliżenie, nie osobny test kolizji każdej nowej pozycji.
- `Ctrl` zachowuje precyzyjną siatkę 0,1 m, ŚPM przełącza snapping. Konstrukcyjne moduły bazy pozostają pominięte.

### G / Full Override
- `G` przełącza clipping **tylko zwykłych mebli** (`Constructable`, bez `ConstructableBase`), także przy kolizjach między szafkami oraz przy ścianach.
- Naprawiony jest również `Builder.CheckSpace` zwracający `bool`; wcześniej miał tylko postfix obsługujący listy i nie odblokowywał decyzji kolizji.
- `G` nie zezwala na ignorowanie ograniczeń konstrukcyjnych modułów bazy, budowanie względem dynamicznego rodzica, obejście sieciowego rozbierania konstrukcji ani usuwanie przedmiotów.
- W istniejącym `ConstrutorZero.Building.ini` można dodać `allow_multiplayer_furniture_clipping=true`; domyślne ustawienie nowej opcji to `true`, ale dopiero naciśnięcie `G` w trakcie stawiania mebla włącza clipping.

## Instalacja / konfiguracja

1. Wykonaj kopię świata serwera. Wyłącz wcześniejsze `Sirro.BZ.MP.ConstrutorZero.dll` i `NativeSnapBuilder.dll`.
2. Umieść **tę samą** `Sirro.BZ.MP.ConstrutorZero.dll` na obu klientach:
   `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`.
3. Uruchom grę ponownie. Konfiguracje w:
   `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\`.
4. Dla fabricatora w pliku `ConstrutorZero.Containers.ini` ustaw:
   `enabled=true`,
   `auto_craft_enabled=true`,
   `manual_prefetch_enabled=false`,
   `auto_build_enabled=false`,
   `auto_place_after_prefetch=false`.
   **Uwaga:** aktualizacja DLL nie nadpisuje starej konfiguracji. Jeśli nadal jest `auto_craft_enabled=false`, crafting ze skrzynek pozostanie wyłączony.
5. Po sprawdzeniu craftingu możesz testować `auto_build_enabled=true` **wyłącznie dla mebli**, zachowując `auto_place_after_prefetch=false`.

## Testy akceptacyjne na kopii świata

- [ ] Po wejściu do świata sprawdź w logu `CFC_STORAGE_SCAN`; `supported_lockers` powinno być większe od zera, jeśli w pobliżu są załadowane zwykłe szafki (technicznie skan obejmuje całą aktywną scenę).
- [ ] Fabricator widzi materiały z `Locker` i `SmallLocker` po włączeniu opcji; craft dwu-/wieloskładnikowy zużywa dokładnie zasoby wymagane przez grę.
- [ ] Zasób wyjęty jednocześnie przez dwóch graczy jest przydzielony tylko jednemu; nie ma duplikacji ani znikania.
- [ ] Obiekt na płaskim terenie nie przesuwa się pionowo po załączeniu siatki; sprawdź różne wysokości podłoża oraz Ctrl/ŚPM.
- [ ] Przesuwanie siatką po skarpie nie podwiesza mebla poza powierzchnią; jeżeli wystąpi, zachowaj lokalizację i log.
- [ ] `G` pozwala nakładać szafki i ustawiać je blisko ścian, ale nie odblokowuje kolizji modułów bazowych.
- [ ] Drugi gracz widzi tę samą konstrukcję po przełączeniu `G`, ponownym dołączeniu i restarcie serwera.
- [ ] Wyłączenie pluginu przywraca oryginalne hooki; brak błędów `CFC_INIT_FAILED`, `SNAP_DISABLED` i `BUILDING_HOOK_FAILED`.

**Wciąż poza zakresem:** atomowe transakcje wieloprzedmiotowe na serwerze, obsługa wszystkich innych magazynów, pełne Attach to Target, pełna zgodność z oryginalnymi SnapBuilder/Building Tweaks oraz automatyczne pobieranie materiałów do modułów bazowych. Nie testuj na głównym zapisie, dopóki nie przejdą testy dwóch klientów.
