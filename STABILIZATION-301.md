# Construtor Zero 0.3.1-rc2 — stabilizacja dla BOT Benson MP 3.0.1

**Status: release candidate po utwardzeniu bezpieczeństwa, nie certyfikowana stabilna wersja.**
Wszystkie bieżące kontrole ABI i symulacje CI muszą przejść, ale rzeczywiste testy na **dwóch klientach z serwerem 3.0.1** nadal muszą być wykonane. Zachowaj zapasową kopię zapisu świata.

## Zakres wtyczki

- Jedna kliencka DLL `Sirro.BZ.MP.ConstrutorZero.dll`; nie wymaga BepInEx, Nautilus ani SML.
- SnapBuilder — ograniczony snap dla niebazowych obiektów (siatka 0,5 / Ctrl 0,1 m, obroty podłogi 45° / Alt 5°). Moduły bazowe pomijane. Ściany i sufity nie są obracane jak podłoga. Odtwarzanie oryginalnej pozycji i rotacji przy błędzie refleksji.
- Building Tweaks — fragment zasięgów budowy i powierzchni mebli. Flagi uprawnień Builder są przywracane po zmianie prefabu oraz zakończeniu budowania. Free placement z ręcznym przestawianiem ghosta jest **wyłączony w multiplayerze**, ponieważ nie przechodzi ponownej walidacji kolizji po zmianie pozycji; dostępny offline.
- Craft From Containers — obsługa **Locker / SmallLocker**: UI fabricatora może pokazać komplet składników w zasięgu, ale nie oszukuje liczników ekwipunku. Po kliknięciu serwer dostaje żądania przeniesienia **pojedynczych przedmiotów**; działanie jest wznawiane dopiero po otrzymaniu ich dokładnego `ItemId` i `TechType` w rzeczywistym ekwipunku. Pozycje/ilości w szafkach pozostają własnością serwera.
- Odroczenie budowania dotyczy **zwykłych mebli**, nie modułów bazowych. Nie wysyła pakietu budowania, dopóki brakuje składników. Automatyczne położenie po pobraniu surowców pozostaje wyłączone domyślnie.

## Krytyczne zabezpieczenia

1. CFC nie aktywuje się bez wymaganych hooków Harmony i nie generuje lokalnych przedmiotów ani nie zwiększa wirtualnych liczników składników.
2. Zakaz powtórnego żądania przedmiotu po timeoutcie **i po ACK** w obrębie jednej sesji; zapobiega odczytaniu przedmiotu ponownie ze starej reprezentacji szafki. **Ograniczenie:** po odłożeniu tego samego `ItemId` z powrotem do szafki może być potrzebne ponowne dołączenie do serwera, aby był dostępny dla CFC.
3. Zamknięcie fabricatora, `OnDeselect`, utrata odblokowania receptury, tryb Creative, zmiana ghosta lub rozłączenie anulują oczekującą operację przed kolejnym transferem. Materiały już prawidłowo przeniesione **pozostają w ekwipunku**.
4. UI wskazuje wyłącznie **lokalnie widoczny** stan schowków. Serwer może odrzucić żądanie, gdy drugi gracz pobrał już przedmiot.
5. Snap nigdy nie pozwala, aby częściowo zmienione argumenty geometrii wyciekły po wyjątku. Globalne flagi `Builder` nie przechodzą z mebli na moduły bazowe.
6. Nieobsłużony wyjątek podczas `Player.Update` lub hooka craftingu **wyłącza CFC do końca sesji**, anuluje kolejki i przywraca zwykłe budowanie; żaden kolejny pakiet transferu nie jest wysyłany przez mod.
7. Automatyczne pobieranie materiałów do mebli nie rozpoczyna się, jeśli lokalnie widoczne szafki nie zawierają kompletnego przepisu. To ogranicza przewidywalne transfery częściowe, lecz **nie eliminuje odrzucenia serwera** przy równoczesnej aktywności innych graczy.

## Czego ten release candidate NIE zapewnia

- **Brak transakcji atomowej i rollbacku całej receptury.** Udostępniony protokół serwera BOT Benson przetwarza transfery po jednym przedmiocie; bez serwerowego rozszerzenia nie da się zagwarantować transakcji wieloskładnikowej. To nie oznacza automatycznej utraty zasobów: poprawnie dostarczone składniki zostają w ekwipunku przy anulowaniu.
- Brak pełnego portu SnapBuilder/Building Tweaks, zwłaszcza pełnego Attach To Target, Full Override, niestandardowych colliderów, Advanced AimTransform i pełnej zgodności z oryginałem.
- Brak bezpiecznego CFC dla wszystkich typów schowków oraz automatycznego craftingu konstrukcji bazowych.
- CI nie uruchamia prawdziwej gry ani dwóch klientów. Nie potwierdza trwałości po restarcie świata ani prawdziwej synchronizacji wizualnej.

## Instalacja

1. Zrób kopię świata serwera i konfiguracji. Wyłącz w kliencie stare Construtor Zero i NativeSnapBuilder DLL, a także oryginalne pluginy tworzące konflikt.
2. Wgraj tę samą DLL do folderu **obu** klientów:
`%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`
3. Uruchom i zamknij grę. Konfiguracje znajdziesz w:
`%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\`
4. `ConstrutorZero.Containers.ini`: `enabled=true` (domyślnie), `auto_craft_enabled=false`, `auto_build_enabled=false`, `manual_prefetch_enabled=false`, `auto_place_after_prefetch=false`.
5. Najpierw sprawdź snap i zwykłe budowanie przy wyłączonym CFC. Następnie włącz `auto_craft_enabled=true` i sprawdź fabrykator z dwoma składnikami w zwykłej szafce. Dopiero na końcu przetestuj `auto_build_enabled=true` z `auto_place_after_prefetch=false`. Włączane opcje wymagają restartu klienta.

## Obowiązkowe testy końcowe dwóch klientów

- [ ] Obaj klienci widzą identyczny snap mebli, a moduły bazowe zachowują nienaruszoną geometrię, także po reconnect i restarcie serwera.
- [ ] Obaj widzą identyczne liczby materiałów przed craftem, po craftcie, po anulowaniu oraz po restarcie świata.
- [ ] Jeden wspólny ostatni materiał: dwie osoby jednocześnie próbują wykonać ten sam crafting; nie pojawia się duplikat.
- [ ] Opóźniony/odmówiony transfer, pełny ekwipunek, wyjście z menu, OnDeselect, utrata łącza, powrót po timeoutcie — brak auto-craftingu bez potwierdzenia.
- [ ] Zwykłe meble po pobraniu materiałów tworzą się tylko raz i są widoczne dla drugiego gracza; **moduły bazowe nie są automatycznie pobierane**.
- [ ] Logi obu klientów i serwera: brak `CFC_INIT_FAILED`, `BUILDING_HOOK_FAILED` i `SNAP_DISABLED`, a wymagana integracja uruchamia się prawidłowo.

**STOP:** przy zniknięciu zasobu, duplikacji, niezgodnej pozycji konstrukcji, błędzie pakietu lub zawieszeniu craftingu przerwij i zachowaj kopię świata oraz logi. Potwierdzona stabilność wymaga przejścia tych testów na prawdziwym serwerze.

## Zmiany 0.3.1-rc2

- Przerwanie wykonania po nieoczekiwanym wyjątku Harmony/refleksji (`CFC_RUNTIME_DISABLED` w logu), bez automatycznego ponawiania.
- Brak rozpoczęcia automatycznego budowania, gdy przed pierwszym żądaniem wiadomo, że nie ma pełnego zestawu składników.
- Test wymuszonej awarii API Unity, czyszczenia oczekujących operacji oraz możliwości dalszego zwykłego budowania.
- Ostatnie CI przed pakowaniem: 182/182 kontrolnych/symulacyjnych testów; nie jest to test serwera 3.0.1 w grze.
