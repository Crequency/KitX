import sys
import os
from datetime import datetime, timedelta, timezone

current_utc_time = datetime.utcnow()
current_utc_time = current_utc_time.replace(tzinfo=timezone.utc)

print("Args list: ", sys.argv)

time_str = sys.argv[1]
days_deference = int(sys.argv[2])

datetime_obj = datetime.fromisoformat(time_str)
datetime_obj = datetime_obj.replace(tzinfo=timezone.utc)

time_difference = current_utc_time - datetime_obj

print("Time delta: ", time_difference)

has_new_commit = time_difference > timedelta(days=days_deference)
env_to_add = "HAS_NEW_COMMIT="

if time_difference > timedelta(days=days_deference):
    print("No new commit found. Check in env var: [env.HAS_NEW_COMMIT].")
    env_to_add = env_to_add + 'false'
else:
    print("New commit found. Check in env var: [env.HAS_NEW_COMMIT].")
    env_to_add = env_to_add + 'true'

command = "echo \"" + env_to_add + "\" >> $env:GITHUB_ENV"

with open('set_env.ps1', 'w', encoding='utf-8') as file:
    file.write(command)
