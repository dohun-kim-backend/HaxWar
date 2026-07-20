.PHONY: build clean restore rebuild run-server run-client test test-perf test-load benchmark docker-up docker-down docker-restart docker-logs docker-ps docker-benchmark k3d-image redis-cluster-check redis-cluster-monitor docker-cluster-up docker-cluster-down docker-cluster-start

# 솔루션 파일 지정
SOLUTION = HexWar.sln

# 프로젝트 파일 지정
SERVER_PROJECT = src/HexWar.Server/HexWar.Server.csproj
CLIENT_PROJECT = src/HexWar.Client/HexWar.Client.csproj
BENCHMARK_PROJECT = tests/HexWar.Benchmarks/HexWar.Benchmarks.csproj
PERF_TEST_PROJECT = tests/HexWar.Performance.Tests/HexWar.Performance.Tests.csproj
LOAD_TEST_PROJECT = tests/HexWar.LoadTests/HexWar.LoadTests.csproj

# ── 1. 빌드 및 복원 명령어 ──
build:
	dotnet build $(SOLUTION)

clean:
	dotnet clean $(SOLUTION)
	@echo "bin 및 obj 캐시 폴더 정리 중..."
	find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
	@echo "정리가 완료되었습니다."

restore:
	dotnet restore $(SOLUTION)

rebuild: clean restore build

# ── 2. 로컬 실행 명령어 (Standalone) ──
run-server:
	dotnet run --project $(SERVER_PROJECT)

run-client:
	dotnet run --project $(CLIENT_PROJECT)

# ── 3. 테스트 및 성능 진단 ──
# 전체 테스트 실행
test:
	dotnet test $(SOLUTION)

# 성능 테스트 단독 실행
test-perf:
	dotnet test $(PERF_TEST_PROJECT)

# 부하 테스트(WebSocket 시뮬레이터) 실행
test-load:
	dotnet run --project $(LOAD_TEST_PROJECT)

# 벤치마크 로컬 실행 (Release 빌드 필수)
benchmark:
	dotnet run -c Release --project $(BENCHMARK_PROJECT)

# ── 4. Docker Compose 인프라 제어 ──
# 모든 컨테이너 기동 (백그라운드)
docker-up:
	docker compose up -d

# 모든 컨테이너 정지 및 볼륨/네트워크 삭제
docker-down:
	docker compose down

# 모든 컨테이너 재시작
docker-restart: docker-down docker-up

# 컨테이너 실시간 로그 확인
docker-logs:
	docker compose logs -f

# 컨테이너 상태 모니터링
docker-ps:
	docker compose ps

# 컨테이너 내에서 벤치마크 테스트 구동
docker-benchmark:
	docker compose run --rm hexwar-benchmarks

# ── 5. k3d 이미지 빌드 및 업로드 ──
K3D_CLUSTER_NAME ?= hexwar-cluster
IMAGE_NAME ?= hexwar-server-1:latest
DOCKERFILE_PATH ?= src/HexWar.Server/Dockerfile

k3d-image:
	docker build -t $(IMAGE_NAME) -f $(DOCKERFILE_PATH) .
	k3d image import $(IMAGE_NAME) -c $(K3D_CLUSTER_NAME)

redis-cluster-check:
	docker exec -it redis-node-1 redis-cli cluster info
	docker exec -it redis-node-1 redis-cli cluster nodes

redis-cluster-monitor:
	docker exec -it redis-node-1 redis-cli monitor

docker-cluster-up:
	docker compose -f docker-compose.cluster.yml up -d --build

docker-cluster-down:
	docker compose -f docker-compose.cluster.yml down

docker-cluster-start: docker-cluster-up
	docker compose -f docker-compose.cluster.yml logs -f

