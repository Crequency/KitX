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

github_env = os.environ.get("GITHUB_ENV", "")

if time_difference > timedelta(days=days_deference):
    print("No new commit found. Check in env var: [env.HAS_NEW_COMMIT].")
    github_env = github_env + '\n' + 'HAS_NEW_COMMIT=false'
else:
    print("New commit found. Check in env var: [env.HAS_NEW_COMMIT].")
    github_env = github_env + '\n' + 'HAS_NEW_COMMIT=true'

os.environ["GITHUB_ENV"] = github_env
