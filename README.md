# Autonomiczne lądowanie rakiety z wykorzystaniem uczenia ze wzmocnieniem (Starship Lander)

Projekt realizowany w ramach przedmiotu **Uczenie się ze wzmocnieniem** (WEiI PRz, 2026/2027).

---

## 1. Opis problemu decyzyjnego
Celem projektu jest nauczenie autonomicznego agenta sterowania rakietą w końcowej fazie lądowania (manewr obrotu i pionowego przyziemienia w stylu SpaceX Starship) na wyznaczonej platformie lądowania.

### Dlaczego problem wymaga uczenia ze wzmocnieniem (RL)?
- **Sekwencyjne decyzje:** Każda zmiana ciągu silnika lub kąta dyszy wpływa na przyszłą pozycję, prędkość i orientację kątową rakiety.
- **Konflikt celów:** Agent musi zrównoważyć szybkie wygaszanie prędkości opadania z minimalizacją zużycia paliwa oraz precyzyjnym ustawieniem pionowym.
- **Dynamika nieliniowa i opóźnienia:** Obrót kadłuba o dużej bezwładności wymaga wcześniejszego planowania działań (brak możliwości natychmiastowej korekty tuż nad ziemią).

---

## 2. Wstępna formalizacja MDP (Markov Decision Process)

- **Agent:** Układ sterowania rakietą (ciąg główny, gimbal silnika, stery RCS/klapy).
- **Środowisko:** Dwuwymiarowa symulacja dynamiki bryły sztywnej (3-DoF: ruch w płaszczyźnie $X-Y$ oraz rotacja wokół osi $Z$) oparta na silniku PhysX w Unity.

### Przestrzeń stanu / obserwacji ($S$):
Wektor obserwacji ciągłych składający się z **10 zmiennych**:
1. **$\Delta x$:** Względna pozycja pozioma względem środka platformy $[m]$.
2. **$\Delta y$:** Względna wysokość nad powierzchnią lądowiska $[m]$.
3. **$v_x$:** Prędkość liniowa w osi poziomej $[m/s]$.
4. **$v_y$:** Prędkość liniowa w osi pionowej $[m/s]$.
5. **$u_x = \sin(\theta)$:** Rzut wektora osi rakiety na oś poziomą (miara odchylenia).
6. **$u_y = \cos(\theta)$:** Rzut wektora osi rakiety na oś pionową ($1.0 = \text{pełny pion}$).
7. **$\omega_z$:** Prędkość kątowa kadłuba $[rad/s]$.
8. **$\phi_{\text{TVC}}$:** Aktualny kąt wychylenia dyszy silnika $[rad]$.
9. **$a_{t-1, \text{thrust}}$:** Poziom ciągu silnika z poprzedniego kroku decyzyjnego $[0.0, 1.0]$.
10. **$m_{\text{fuel}}$:** Znormalizowany pozostały zasób paliwa $[0.0, 1.0]$.

### Przestrzeń akcji ($A$):
Ciągła przestrzeń sterowania $\mathcal{A} = [-1.0, 1.0]^3$:
- **$a_0 \in [-1, 1]$:** Ciąg silnika głównego, skalowany do zakresu $[0, 1] \cdot F_{\max}$ ($F_{\max} = 30\text{ kN}$).
- **$a_1 \in [-1, 1]$:** Wychylenie wektora ciągu dyszy (gimbal TVC) w zakresie $[-\phi_{\max}, \phi_{\max}]$, gdzie $\phi_{\max} = 20^\circ$.
- **$a_2 \in [-1, 1]$:** Moment obrotowy sterów manewrowych RCS/klap aerodynamicznych $\tau = a_2 \cdot \tau_{\max}$.

### Funkcja nagrody ($R$):
Krokowa funkcja nagrody:
$$r_t = -0.002 - 0.001 \cdot \|\Delta \mathbf{p}\| + w_y \cdot \max(0, \cos\theta) \cdot 0.04 - 0.0005 \cdot T_{\text{norm}} + r_{\text{terminal}}$$
gdzie $w_y = \min\left(1.0, \frac{35.0}{\max(\Delta y, 1.0)}\right)$ jest wagą wysokościową.

- **Kary krokowe:** $-0.002$ za każdy krok symulacji (zapobieganie zawisaniu w powietrzu).
- **Kara za odległość:** proporcjonalna do odległości euklidesowej $\|\Delta \mathbf{p}\|$ od środka lądowiska.
- **Kształtowanie nagrody (Reward Shaping):** nagroda za pionizację kadłuba $\cos(\theta)$ aktywowana przy zejściu poniżej $35\text{ m}$.
- **Kara za paliwo:** $-0.0005 \cdot T_{\text{norm}}$ wymuszająca opóźniony, efektywny zapłon (*suicide burn*).
- **Nagrody/kary terminalne ($r_{\text{terminal}}$):**
  - $+150.0$ za udane lądowanie ($|v_y| < 5.0\text{ m/s}$, $|v_x| < 1.5\text{ m/s}$, $\cos\theta > 0.95$).
  - $-30.0$ za zbyt twarde przyziemienie lub przewrócenie kadłuba.
  - $-50.0$ za uderzenie poza platformą lądowania.
  - $-100.0$ za opuszczenie dozwolonej strefy testowej.

### Warunki zakończenia epizodu:
1. Udany kontakt z platformą lądowania przy spełnieniu norm prędkości i kąta.
2. Kolizja z podłożem przy niedozwolonych parametrach (zbyt duża prędkość, duży przechył).
3. Opuszczenie strefy lotu (odchylenie poziome $|\Delta x| > 50\text{ m}$ lub pułap $y > 150\text{ m}$).
4. Upłynięcie limitu czasu ($1000$ kroków decyzyjnych).

---

## 3. Planowany Agent Bazowy (Baseline)
Do oceny i porównania działania agenta RL przygotowany zostanie:
- **Agent heurystyczny (kontroler PID / PD):** algorytm regułowy korygujący przechył kątowy dyszą oraz uruchamiający ciąg hamujący przy określonym pułapie.
- **Agent losowy:** generujący losowe sygnały sterujące jako skrajny punkt odniesienia.

---

## 4. Wykorzystany algorytm RL
- **Algorytm docelowy:** PPO (Proximal Policy Optimization).
- **Framework:** Unity ML-Agents z backendem PyTorch.

