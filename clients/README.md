# Client integration

Local Display Server exposes stable HLS URLs and a small JSON discovery API so client applications can be implemented independently.

## Discover displays

```http
GET /api/clients/tvs
```

Each item contains:

- `id`
- `name`
- `streamUrl`

## Playback

For display ID `1`:

```text
/hls/tv1/index.m3u8
```

Clients should treat this as a live HLS stream. The server performs the media loop; clients should not add their own looping logic.

## Web/TV client

A simple HTML5 client is included at:

```text
/vidaa/
```

It can serve as a reference implementation for other TV platforms.

## Roku

The public repository intentionally does not include the original venue-specific Roku ZIP. A Roku client can query `/api/roku/tvs` or `/api/clients/tvs`, store the selected ID locally, and play the corresponding HLS URL.

## Other platforms

Any HLS-capable client can be used, including VLC, embedded webviews, Android/Android TV applications, custom desktop clients, and many smart-TV browsers.
