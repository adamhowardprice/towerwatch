IMAGE := towerarchers-build
# Named volumes keep NuGet packages and Linux build output out of the host tree.
RUN := docker run --rm -v "$(CURDIR)":/work -v towerarchers-nuget:/root/.nuget $(IMAGE)
SERVER_DIR := .valheim-server

.PHONY: image fetch fetch-full decompile build smoke package clean

image:
	docker build -t $(IMAGE) .

# Downloads only the managed DLLs of the dedicated server (Steam app 896660).
fetch: image
	$(RUN) sh -c 'printf "regex:^valheim_server_Data/Managed/.*\\.dll$$\n" > /tmp/files.txt && DepotDownloader -app 896660 -os linux -filelist /tmp/files.txt -dir /work/$(SERVER_DIR)'

# Decompiles the game assembly to .decompiled/ to check class and field names.
decompile: image
	$(RUN) sh -c 'mkdir -p /work/.decompiled && ilspycmd -p -o /work/.decompiled /work/$(SERVER_DIR)/valheim_server_Data/Managed/assembly_valheim.dll'

build: image
	$(RUN) dotnet build TowerArchers.sln -c Release -p:VALHEIM_INSTALL=/work/$(SERVER_DIR)

# Downloads the whole dedicated server. The smoke test needs this, at about 2 GB.
fetch-full: image
	$(RUN) DepotDownloader -app 896660 -os linux -dir /work/$(SERVER_DIR)

# Boots the real server with the mod. Run fetch-full first.
smoke: build
	$(RUN) dotnet build smoke/Scenario/Scenario.csproj -c Release -p:VALHEIM_INSTALL=/work/$(SERVER_DIR)
	docker build --platform linux/amd64 -t $(IMAGE)-smoke smoke
	docker run --rm --platform linux/amd64 -v "$(CURDIR)":/work $(IMAGE)-smoke sh /work/smoke/run.sh

package: build
	$(RUN) sh thunderstore/package.sh

clean:
	rm -rf src/TowerArchers/bin src/TowerArchers/obj dist
