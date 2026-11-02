# Autonomiczne lądowanie rakiety z wykorzystaniem uczenia ze wzmocnieniem (Starship Lander)

Projekt realizowany w ramach przedmiotu **Uczenie się ze wzmocnieniem** (WEiI PRz, 2026/2027).

---

## 1. Opis problemu decyzyjnego
Celem projektu jest nauczenie autonomicznego agenta sterowania rakietą w końcowej fazie lądowania (manewr obrotu i pionowego przyziemienia w stylu Starship) na wyznaczonej platformie lądowania.

### Dlaczego problem wymaga uczenia ze wzmocnieniem (RL)?
- **Sekwencyjne decyzje:** Każda zmiana ciągu silnika lub kąta dyszy wpływa na przyszłą pozycję, prędkość i orientację kątową rakiety.
- **Konflikt celów:** Agent musi zrównoważyć szybkie wygaszanie prędkości opadania z minimalizacją zużycia paliwa oraz precyzyjnym ustawieniem pionowym.
- **Dynamika nieliniowa i opóźnienia:** Obrót kadłuba o dużej bezwładności wymaga wcześniejszego planowania działań (brak możliwości natychmiastowej korekty tuż nad ziemią).

---

## 2. Wstępna formalizacja MDP (Markov Decision Process)

- **Agent:** Układ sterowania rakietą (ciąg główny, gimbal silnika, stery).
- **Środowisko:** Symulacja fizyczna dynamiki bryły sztywnej w silniku Unity.

### Przestrzeń stanu / obserwacji ($S$):
Wektor obserwacji ciągłych ($12$ zmiennych):
1. **Względna pozycja do lądowiska:** $(\Delta x, \Delta y, \Delta z)$
2. **Prędkości liniowe:** $(v_x, v_y, v_z)$
3. **Prędkości kątowe:** $(\omega_x, \omega_y, \omega_z)$
4. **Wektor orientacji pionowej:** wychylenie osi wzdłużnej rakiety względem pionu.

### Przestrzeń akcji ($A$):
Ciągła przestrzeń sterowania w zakresie $[-1.0, 1.0]$:
- $a_0$: Ciąg silnika głównego (mapowany liniowo na zakres $[0, 100\%]$ mocy maksymalnej).
- $a_1$: Kąt wychylenia wektora ciągu dyszy w płaszczyźnie bocznej (TVC Gimbal).
- $a_2$: Moment siły klap aerodynamicznych / silniczków manewrowych RCS (stabilizacja kątowa).

### Funkcja nagrody ($R$):
- **Kary krokowe:** niewielka ujemna nagroda za każdy krok symulacji ($-0.002$) wymuszająca szybkie i sprawne lądowanie bez niepotrzebnego zawisu.
- **Kształtowanie nagrody (Reward Shaping):**
  - Kara proporcjonalna do odległości euklidesowej od środka platformy.
  - Nagroda za utrzymywanie kadłuba w pionie ($\cos(\theta)$ blisko $1.0$), rosnąca w miarę zbliżania się do powierzchni.
- **Warunki końcowe:**
  - $+150.0$ za udane lądowanie (przyziemienie w obrębie platformy z prędkością pionową $v_y < 5\text{ m/s}$ i odchyleniem od pionu mniejszym niż $10^\circ$).
  - $-50.0$ za uderzenie z nadmierną prędkością lub przewrócenie kadłuba.
  - $-100.0$ za ucieczkę poza dozwolony obszar testowy.

### Warunki zakończenia epizodu:
1. Kontakt rakiety z podłożem lub platformą lądowania.
2. Przekroczenie granic przestrzeni symulacji (np. odchylenie boczne $> 60\text{ m}$ lub pułap $> 200\text{ m}$).
3. Osiągnięcie limitu czasu (maksymalna liczba kroków w epizodzie: np. 1000 kroków).

---

## 3. Planowany Agent Bazowy (Baseline)
Do oceny i porównania działania agenta RL przygotowany zostanie:
- **Agent heurystyczny (kontroler PID / PD):** prosty algorytm regułowy korygujący przechył kątowy dyszą oraz uruchamiający ciąg hamujący przy określonym pułapie.
- **Agent losowy:** generujący losowe sygnały sterujące jako skrajny punkt odniesienia.

---

## 4. Wykorzystany algorytm RL
- **Algorytm docelowy:** PPO (Proximal Policy Optimization).
- **Framework:** Unity ML-Agents z backendem PyTorch.

