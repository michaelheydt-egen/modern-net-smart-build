docker run \\
    --volume=nexus-data:/nexus-data \\
    --volume=/nexus-data \\
    --network=cicd-net \\
    -p 8081:8081 \\
    -p 8082:8082 \\
    -d sonatype/nexus3:latest