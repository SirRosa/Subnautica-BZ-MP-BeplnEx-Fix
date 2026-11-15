# Construtor Zero v0.2.1-test — pierwszy kontrolowany test

**Projekt:** Subnautica Below Zero + BOT Benson Multiplayer 3.0.1.
**Stan:** DLL tylko do pierwszych testów na *kopii zapisu*. Nie jest to pełny port SnapBuilder, Building Tweaks ani Craft From Containers. GitHub Actions nie uruchamia prawdziwej gry.

## Co realnie zawiera DLL
- Natywny plugin kliencki; bez BepInEx, Nautilus, SML i bez pakowania cudzych DLL.
- Uproszczony Snap: siatka 0,5 m, Ctrl 0,1 m, obrót 45° / Alt 5° tylko na niemal poziomych powierzchniach.
- Building Tweaks: poszerzenie zasięgów, częściowe rozszerzone powierzchnie dla mebli, podstawowe manualne przesuwanie ghosta, zabezpieczenie niszczenia, PowerConsumer.
- CFC: **tylko Locker i SmallLocker**, transfer do rzeczywistego ekwipunku za pośrednictwem zdarzenia multiplayera; dopiero po otrzymaniu dokładnego `ItemId` w `Inventory.OnAddItem` wznawiany jest oryginalny crafting.
- Auto-build (tylko zwykłe meble/wyposażenie) może wstrzymać kliknięcie i pobrać składniki; oryginalny `Builder.TryPlace` pozostaje własnością multiplayera. Bez automatycznego wysyłania pakietu podczas oczekiwania na składniki.
- Naprawy bezpieczeństwa: brak ponawiania żądań dla niepotwierdzonego ID, zamknięcie oczekujących akcji po rozłączeniu, blokada kliknięć przy chwilowej zmianie Creative/canPlace.
- Opcje `auto_craft_enabled`, `auto_build_enabled`, `auto_place_after_prefetch`, `manual_prefetch_enabled` są **false domyślnie**.

## Co nadal nie działa / ograniczenia
- **Brak pełnej transakcji atomowej i rollbacku składników** po częściowej dostawie. Dostarczone materiały pozostają w ekwipunku, nawet gdy crafting zostaje anulowany.
- Materiały do `ConstructableBase` i innych modułów bazy nie są pobierane automatycznie; obsługiwane są tylko zwykłe przedmioty budowane.
- Brak innych typów schowków, odświeżania liczb składników UI z magazynów oraz craftingu bezpośrednio z magazynu.
- Oryginalny `Attach to Target`, `Full Override`, zaawansowane kolidery, raycasty i `AimTransform` nie zostały przeniesione. Nie włączać override world-state na wspólnym zapisie.
- Obecne testy CI symulują multiplayer; nie zastępują rzeczywistej gry, dwóch klientów i serwera 3.0.1.

## Instalacja
1. Zrób kopię *zapisu serwera* i obu konfiguracji klientów. Nie używaj aktywnego wspólnego świata.
2. Na obu komputerach usuń stare DLL `Sirro.BZ.MP.NativeSnapBuilder.dll` i poprzedni `Sirro.BZ.MP.ConstrutorZero.dll` z `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Plugins\`.
3. Na obu komputerach skopiuj **wyłącznie** `Sirro.BZ.MP.ConstrutorZero.dll` z artefaktu GitHub Actions.
4. Uruchom i zamknij grę, aby utworzyć pliki konfiguracyjne w `%APPDATA%\.botbenson\Subnautica Below Zero\Game\Config\`.
5. Pierwszy test: ustaw `auto_craft_enabled=false`, `auto_build_enabled=false`, `auto_place_after_prefetch=false`; sprawdź czy zwykłe budowanie i snaps działają oraz czy obaj gracze widzą identyczne pozycje.
6. Następnie ustaw w `ConstrutorZero.Containers.ini` **`auto_craft_enabled=true`**, zrestartuj obu klientów i testuj fabricator z Titanem oraz innym składnikiem w zwykłym Locker.
7. Po poprawnym craftingu ustaw **`auto_build_enabled=true`** oraz **`auto_place_after_prefetch=false`**; testuj wyłącznie zwykły mebel. Po dostarczeniu surowców kliknij raz, aby wywołać normalny mechanizm budowy.
8. Opcjonalnie `manual_prefetch_enabled=true` włącza F6 dla przypiętych przepisów. Używaj wyłącznie po poprzednich kontrolach.

## Scenariusze obowiązkowe — obaj gracze na kopii świata
- [ ] Weryfikacja logów: `ENABLED`, `BUILDING_PATCHES_INSTALLED`, `CFC_READY`, brak nieoczekiwanych `HOOK_FAILED`.
- [ ] Snap na podłodze; pozycja oraz orientacja identyczna u obu klientów, również po ponownym dołączeniu.
- [ ] Craft z wieloma różnymi składnikami z Locker/SmallLocker: tyle samo sztuk przed i po, żadnych darmowych.
- [ ] Budowanie zwykłego mebla z brakującymi składnikami, bez pakietu konstrukcji zanim dostaną się do ekwipunku.
- [ ] Anulowanie craftingu podczas pobierania składników; brak automatycznego craftu po zamknięciu menu.
- [ ] Jednoczesna próba pobrania ostatniego takiego samego przedmiotu przez dwóch graczy; zasób może zostać przypisany co najwyżej raz.
- [ ] Pełny ekwipunek, odmowa transferu, timeout, zerwanie sesji i ponowne dołączenie.
- [ ] Restart serwera, odtworzenie zapisu i weryfikacja liczby przedmiotów i położenia konstrukcji.

**STOP / NO-GO:** jeśli którykolwiek gracz widzi inny przedmiot lub pozycję, pojawia się duplikat, znika materiał, zawiesza się podgląd konstrukcji lub występuje stale powtarzane żądanie — przerwij test. Zachowaj logi obu klientów i serwera oraz dokładną sekwencję zdarzeń.

## CI i bezpieczeństwo
Przed opublikowaniem pierwszej DLL wymagamy: zielonego CI na finalnym SHA, bez błędów kompilacji, i wszystkich symulacji w tym czasie. Test w grze nadal należy wykonać ręcznie. Paczka nie zawiera plików gry ani multiplayera.
