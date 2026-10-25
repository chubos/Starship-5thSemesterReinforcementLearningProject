import time
from stable_baselines3 import PPO
from starship_env import StarshipLandingEnv

env = StarshipLandingEnv(render_mode="human")
model = PPO.load("starship_ppo")

obs, info = env.reset()
for _ in range(1500):
    action, _states = model.predict(obs, deterministic=True)
    obs, reward, terminated, truncated, info = env.step(action)
    
    if terminated or truncated:
        time.sleep(0.5)
        obs, info = env.reset()

env.close()