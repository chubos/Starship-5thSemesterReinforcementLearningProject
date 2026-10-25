from stable_baselines3 import PPO
from starship_env import StarshipLandingEnv

env = StarshipLandingEnv()
model = PPO.load("starship_ppo", env=env)

print("Dotrenowywanie...")
model.learn(total_timesteps=1_000_000, reset_num_timesteps=False, progress_bar=True)
model.save("starship_ppo")
print("Model zapisany!")
