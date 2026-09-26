# Samba server with share "Storage" for user storage/storage
FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends samba && rm -rf /var/lib/apt/lists/* \
 && useradd -M storage && (echo storage; echo storage) | smbpasswd -a -s storage \
 && mkdir -p /srv/storage && chown storage /srv/storage \
 && printf '[Storage]\n   path = /srv/storage\n   writable = yes\n   valid users = storage\n' >> /etc/samba/smb.conf
EXPOSE 445
CMD ["smbd", "--foreground", "--no-process-group", "--debug-stdout"]
