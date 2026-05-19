docker build -f .\Dockerfile-build -t netsdk10:latest .
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock alpine ls -la /var/run/docker.sock
