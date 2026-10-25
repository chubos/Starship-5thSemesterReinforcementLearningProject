import gymnasium as gym
from gymnasium import spaces
import numpy as np
import pygame
import math

class StarshipLandingEnv(gym.Env):
    metadata = {"render_modes": ["human", "rgb_array"], "render_fps": 50}

    def __init__(self, render_mode=None):
        super().__init__()
        self.render_mode = render_mode

        # Parametry symulacji fizycznej
        self.dt = 0.02          # 50 Hz krok symulacji
        self.g = 9.81           # Grawitacja (m/s^2)
        self.mass = 1000.0      # Masa kadłuba (kg)
        self.length = 20.0      # Długość rakiety (m)
        self.inertia = (1.0 / 12.0) * self.mass * (self.length ** 2)  # Moment bezwładności pręta
        self.max_thrust = 22000.0  # Max ciąg (TWR ~ 2.2)
        self.max_gimbal = np.radians(20)  # Max wychylenie silnika (+/- 20 stopni)
        self.max_rcs_torque = 8000.0      # Moment obrotowy z RCS / klap (Nm)

        # Przestrzeń akcji (ciągła, 3 wartości od -1.0 do 1.0):
        # 1. Ciąg główny: [-1, 1] mapowane na [0, max_thrust]
        # 2. Kąt silnika (gimbal): [-1, 1] mapowane na [-max_gimbal, max_gimbal]
        # 3. Moment RCS / aero: [-1, 1] (lewo / prawo)
        self.action_space = spaces.Box(
            low=-1.0, high=1.0, shape=(3,), dtype=np.float32
        )

        # Przestrzeń obserwacji:
        # [x, y, vx, vy, theta, omega, gimbal_angle]
        # x: pozycja pozioma (-200 do 200 m)
        # y: wysokość (0 do 300 m)
        # vx, vy: prędkości (m/s)
        # theta: kąt od pionu (rad, 0 = pionowo do góry)
        # omega: prędkość kątowa (rad/s)
        # gimbal: aktualny kąt silnika
        high = np.array([200.0, 300.0, 100.0, 100.0, np.pi, 20.0, self.max_gimbal], dtype=np.float32)
        self.observation_space = spaces.Box(low=-high, high=high, dtype=np.float32)

        # Pygame setup
        self.window = None
        self.clock = None
        self.screen_width = 800
        self.screen_height = 700
        self.scale = 2.0  # 1 metr = 2 piksele

    def _get_obs(self):
        return np.array([
            self.x, self.y, self.vx, self.vy,
            self.theta, self.omega, self.gimbal
        ], dtype=np.float32)

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)

        # Stan początkowy: rakieta spada z wysokości 200m, lekko przechylona (np. po fazie belly-flop)
        self.x = self.np_random.uniform(-40.0, 40.0)
        self.y = self.np_random.uniform(160.0, 210.0)
        self.vx = self.np_random.uniform(-10.0, 10.0)
        self.vy = self.np_random.uniform(-35.0, -15.0)  # Spada w dół
        self.theta = self.np_random.uniform(-np.pi / 3, np.pi / 3)  # Przechylenie +/- 60 st.
        self.omega = self.np_random.uniform(-0.5, 0.5)
        self.gimbal = 0.0

        self.step_count = 0
        self.max_steps = 600  # 12 sekund symulacji

        obs = self._get_obs()
        info = {}

        if self.render_mode == "human":
            self._render_frame()

        return obs, info

    def step(self, action):
        self.step_count += 1

        # 1. Dekodowanie akcji
        raw_thrust, raw_gimbal, raw_rcs = action
        # Ciąg od 0 do max_thrust
        thrust = (raw_thrust + 1.0) / 2.0 * self.max_thrust
        # Kąt wektora ciągu względem osi rakiety
        self.gimbal = raw_gimbal * self.max_gimbal
        # Moment obrotowy z RCS
        rcs_torque = raw_rcs * self.max_rcs_torque

        # 2. Fizyka - Siły i momenty
        # Ciąg działa na dolny koniec kadłuba (ramię r = length / 2)
        # Kąt ciągu w układzie świata:
        thrust_angle_world = self.theta + self.gimbal

        Fx = -thrust * np.sin(thrust_angle_world)
        Fy = thrust * np.cos(thrust_angle_world) - (self.mass * self.g)

        # Moment obrotowy wytwarzany przez silnik z gimbalem
        # T = r x F = -(L/2) * thrust * sin(gimbal)
        thrust_torque = -(self.length / 2.0) * thrust * np.sin(self.gimbal)
        total_torque = thrust_torque + rcs_torque

        # 3. Integracja numeryczna (Euler)
        ax = Fx / self.mass
        ay = Fy / self.mass
        alpha = total_torque / self.inertia

        self.vx += ax * self.dt
        self.vy += ay * self.dt
        self.x += self.vx * self.dt
        self.y += self.vy * self.dt

        self.omega += alpha * self.dt
        self.theta += self.omega * self.dt

        # Normalizacja kąta theta do przedziału [-pi, pi]
        self.theta = (self.theta + np.pi) % (2 * np.pi) - np.pi

        # 4. Sprawdzenie warunków zakończenia
        ground_level = self.length / 2.0  # Rakieta dotyka ziemi podstawą
        touchdown = self.y <= ground_level
        out_of_bounds = abs(self.x) > 180 or self.y > 280
        timed_out = self.step_count >= self.max_steps

        terminated = False
        truncated = timed_out

        # 5. Funkcja Nagrody (Reward Shaping)
        reward = 0.0

        # Nagradzanie za bycie blisko środka (x=0) i orientację pionową (theta=0)
        reward -= 0.01 * (abs(self.x) + abs(self.theta) * 10.0)
        # Mała kara za zużycie paliwa (ciąg)
        reward -= 0.0001 * thrust

        if touchdown:
            terminated = True
            # Warunki udanego lądowania:
            safe_v = abs(self.vy) < 4.0 and abs(self.vx) < 2.0
            safe_angle = abs(self.theta) < np.radians(10)
            close_to_center = abs(self.x) < 8.0

            if safe_v and safe_angle and close_to_center:
                reward += 200.0  # Idealne lądowanie!
            elif safe_v and safe_angle:
                reward += 80.0   # Miękkie lądowanie poza wyznaczonym punktem
            else:
                reward -= 100.0  # Katastrofa / kraksa

        elif out_of_bounds:
            terminated = True
            reward -= 100.0

        if self.render_mode == "human":
            self._render_frame()

        return self._get_obs(), reward, terminated, truncated, {}

    def _render_frame(self):
        if self.window is None:
            pygame.init()
            if self.render_mode == "human":
                self.window = pygame.display.set_mode((self.screen_width, self.screen_height))
                pygame.display.set_caption("Starship 2D Landing")
            self.clock = pygame.time.Clock()

        canvas = pygame.Surface((self.screen_width, self.screen_height))
        canvas.fill((20, 24, 35))  # Nocne niebo

        # Przeliczenie współrzędnych świata na piksele ekranu
        origin_x = self.screen_width // 2
        ground_y_px = self.screen_height - 60

        def to_screen(x, y):
            px = int(origin_x + x * self.scale)
            py = int(ground_y_px - y * self.scale)
            return px, py

        # Rysowanie lądowiska
        pad_w = 40 * self.scale
        pygame.draw.rect(canvas, (60, 65, 80), (origin_x - pad_w // 2, ground_y_px, pad_w, 20))
        pygame.draw.line(canvas, (240, 200, 50), (origin_x - 15, ground_y_px), (origin_x + 15, ground_y_px), 3)

        # Wierzchołki kadłuba rakiety (prostokąt ze ściętym dziobem)
        hw = 3.0  # pół-szerokość (m)
        hl = self.length / 2.0  # pół-wysokość (m)
        corners = [
            (-hw, -hl), (hw, -hl), (hw, hl - 2), (0, hl + 2), (-hw, hl - 2)
        ]

        # Rotacja wierzchołków
        cos_t, sin_t = np.cos(self.theta), np.sin(self.theta)
        rotated_pts = []
        for cx, cy in corners:
            rx = cx * cos_t - cy * sin_t + self.x
            ry = cx * sin_t + cy * cos_t + self.y
            rotated_pts.append(to_screen(rx, ry))

        # Rysowanie kadłuba (srebrzysty Starship)
        pygame.draw.polygon(canvas, (210, 215, 225), rotated_pts)
        pygame.draw.polygon(canvas, (100, 105, 115), rotated_pts, 2)

        # Płomień silnika (jeśli gimbal i ciąg są aktywne)
        bottom_x = self.x - (-hl) * sin_t
        bottom_y = self.y + (-hl) * cos_t
        bottom_px = to_screen(bottom_x, bottom_y)

        # Wektor ognia
        fire_angle = self.theta + self.gimbal
        flame_len = 12.0  # Długość płomienia w pikselach
        flame_tip = (
            bottom_px[0] + flame_len * np.sin(fire_angle),
            bottom_px[1] + flame_len * np.cos(fire_angle)
        )
        pygame.draw.line(canvas, (255, 120, 20), bottom_px, flame_tip, 4)

        if self.render_mode == "human":
            self.window.blit(canvas, (0, 0))
            pygame.event.pump()
            self.clock.tick(self.metadata["render_fps"])
            pygame.display.flip()

    def close(self):
        if self.window is not None:
            pygame.quit()
            self.window = None