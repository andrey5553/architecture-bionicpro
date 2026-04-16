# setup_minio_public.py
from minio import Minio
import json
import sys

def setup_public_bucket():
    try:
        # Подключение к MinIO
        client = Minio(
            "localhost:9002",
            access_key="minioadmin",
            secret_key="minioadmin",
            secure=False
        )
        
        bucket_name = "reports"
        
        # Проверяем существование bucket
        if not client.bucket_exists(bucket_name):
            print(f"Создаю bucket '{bucket_name}'...")
            client.make_bucket(bucket_name)
        else:
            print(f"Bucket '{bucket_name}' уже существует")
        
        # Политика публичного доступа (readonly)
        policy = {
            "Version": "2012-10-17",
            "Statement": [
                {
                    "Effect": "Allow",
                    "Principal": {"AWS": ["*"]},
                    "Action": ["s3:GetObject"],
                    "Resource": [f"arn:aws:s3:::{bucket_name}/*"]
                }
            ]
        }
        
        # Устанавливаем политику
        print("Устанавливаю публичный доступ...")
        client.set_bucket_policy(bucket_name, json.dumps(policy))
        
        print(f"✅ Bucket '{bucket_name}' теперь публичный!")
        return True
        
    except Exception as e:
        print(f"❌ Ошибка: {e}")
        return False

if __name__ == "__main__":
    success = setup_public_bucket()
    sys.exit(0 if success else 1)
    