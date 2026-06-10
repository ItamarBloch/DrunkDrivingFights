/**
 * DrunkDrivingFights — Matchmaking Server
 *
 * Lightweight REST API that stores active game sessions so clients anywhere
 * on the internet can find and join hosted games.
 *
 * Endpoints:
 *   POST   /sessions          — host registers a new session
 *   GET    /sessions          — client fetches all active sessions
 *   PATCH  /sessions/:id      — host sends a heartbeat (+ updates player count)
 *   DELETE /sessions/:id      — host unregisters the session
 *
 * Sessions expire automatically 60 seconds after the last heartbeat.
 *
 * ── Quick Start ──────────────────────────────────────────────────────────────
 *   npm install
 *   node matchmaking-server.js
 *
 * ── Free Deployment Options ──────────────────────────────────────────────────
 *   Railway  : railway up
 *   Render   : connect GitHub repo, set build=npm install, start=node matchmaking-server.js
 *   Fly.io   : fly launch --no-deploy, fly deploy
 *
 * Set the PORT environment variable if your host requires a specific port.
 * ────────────────────────────────────────────────────────────────────────────
 */

const http = require('http');
const crypto = require('crypto');

const PORT = parseInt(process.env.PORT || '3000', 10);
const SESSION_TTL_MS = 60_000; // expire after 60s without a heartbeat

// ── In-memory session store ───────────────────────────────────────────────────
// Map<sessionId, SessionRecord>
const sessions = new Map();

/**
 * @typedef {Object} SessionRecord
 * @property {string} id
 * @property {string} token          - secret known only to the host, required for PATCH/DELETE
 * @property {string} address        - public IP inferred from the registration request
 * @property {string} roomName
 * @property {number} maxPlayers
 * @property {number} currentPlayers
 * @property {string} mapName
 * @property {boolean} hasPassword
 * @property {string} roomCode
 * @property {number} port
 * @property {string} relayJoinCode  - Unity Relay join code (empty if LAN-only)
 * @property {number} serverId       - mirrors Mirror's discovery serverId (long → JS number)
 * @property {number} lastHeartbeat  - Date.now() timestamp
 */

// ── Helpers ───────────────────────────────────────────────────────────────────

function generateId() {
    return crypto.randomBytes(8).toString('hex');
}

function generateToken() {
    return crypto.randomBytes(24).toString('hex');
}

/**
 * Remove sessions that haven't sent a heartbeat within SESSION_TTL_MS.
 * Called on every write request so stale entries don't pile up.
 */
function purgeExpired() {
    const cutoff = Date.now() - SESSION_TTL_MS;
    for (const [id, session] of sessions) {
        if (session.lastHeartbeat < cutoff) {
            sessions.delete(id);
            console.log(`[Server] Session expired: ${id} (${session.roomName})`);
        }
    }
}

function getClientIp(req) {
    // Respect reverse-proxy headers (Railway, Render, etc.)
    const forwarded = req.headers['x-forwarded-for'];
    if (forwarded) return forwarded.split(',')[0].trim();
    return req.socket.remoteAddress || '0.0.0.0';
}

function readBody(req) {
    return new Promise((resolve, reject) => {
        let data = '';
        req.on('data', chunk => (data += chunk));
        req.on('end', () => {
            try { resolve(data ? JSON.parse(data) : {}); }
            catch (e) { reject(e); }
        });
        req.on('error', reject);
    });
}

function send(res, status, body) {
    const json = JSON.stringify(body);
    res.writeHead(status, {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(json),
        // Allow Unity's UnityWebRequest from any origin
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, PATCH, DELETE, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type',
    });
    res.end(json);
}

// ── Route handlers ────────────────────────────────────────────────────────────

/**
 * POST /sessions
 * Body: { roomName, maxPlayers, currentPlayers, mapName, hasPassword, roomCode, port }
 * Response: { id, token }
 */
async function handleRegister(req, res) {
    let body;
    try { body = await readBody(req); }
    catch { return send(res, 400, { error: 'Invalid JSON' }); }

    purgeExpired();

    const id = generateId();
    const token = generateToken();
    const address = getClientIp(req);

    /** @type {SessionRecord} */
    const session = {
        id,
        token,
        address,
        roomName:       String(body.roomName       || 'Unnamed Room'),
        maxPlayers:     Number(body.maxPlayers)     || 6,
        currentPlayers: Number(body.currentPlayers) || 1,
        mapName:        String(body.mapName         || ''),
        hasPassword:    Boolean(body.hasPassword),
        roomCode:       String(body.roomCode        || ''),
        port:           Number(body.port)           || 7777,
        relayJoinCode:  String(body.relayJoinCode   || ''),
        serverId:       Date.now(),   // unique enough for deduplication
        lastHeartbeat:  Date.now(),
    };

    sessions.set(id, session);
    console.log(`[Server] Session registered: ${id} | "${session.roomName}" | ${address}:${session.port} | ${session.currentPlayers}/${session.maxPlayers}`);

    send(res, 201, { id, token });
}

/**
 * GET /sessions
 * Response: array of public session data (token is NOT included)
 */
function handleList(req, res) {
    purgeExpired();

    const list = [];
    for (const s of sessions.values()) {
        list.push({
            id:             s.id,
            address:        s.address,
            roomName:       s.roomName,
            maxPlayers:     s.maxPlayers,
            currentPlayers: s.currentPlayers,
            mapName:        s.mapName,
            hasPassword:    s.hasPassword,
            roomCode:       s.roomCode,
            port:           s.port,
            relayJoinCode:  s.relayJoinCode,
            serverId:       s.serverId,
        });
    }

    send(res, 200, list);
}

/**
 * PATCH /sessions/:id
 * Body: { token, currentPlayers }
 * Updates the heartbeat timestamp and player count.
 */
async function handleHeartbeat(req, res, id) {
    let body;
    try { body = await readBody(req); }
    catch { return send(res, 400, { error: 'Invalid JSON' }); }

    const session = sessions.get(id);
    if (!session) return send(res, 404, { error: 'Session not found' });
    if (session.token !== body.token) return send(res, 403, { error: 'Forbidden' });

    session.currentPlayers = Number(body.currentPlayers) ?? session.currentPlayers;
    session.lastHeartbeat  = Date.now();

    send(res, 200, { ok: true });
}

/**
 * DELETE /sessions/:id
 * Body: { token }
 */
async function handleUnregister(req, res, id) {
    let body;
    try { body = await readBody(req); }
    catch { return send(res, 400, { error: 'Invalid JSON' }); }

    const session = sessions.get(id);
    if (!session) return send(res, 404, { error: 'Session not found' });
    if (session.token !== body.token) return send(res, 403, { error: 'Forbidden' });

    sessions.delete(id);
    console.log(`[Server] Session unregistered: ${id} (${session.roomName})`);
    send(res, 200, { ok: true });
}

// ── Main HTTP dispatcher ──────────────────────────────────────────────────────

const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, `http://localhost:${PORT}`);
    const path = url.pathname.replace(/\/$/, ''); // strip trailing slash

    // CORS pre-flight
    if (req.method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, PATCH, DELETE, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type',
        });
        return res.end();
    }

    // Health check
    if (path === '/health' && req.method === 'GET') {
        return send(res, 200, { status: 'ok', sessions: sessions.size });
    }

    // POST /sessions
    if (path === '/sessions' && req.method === 'POST') {
        return handleRegister(req, res);
    }

    // GET /sessions
    if (path === '/sessions' && req.method === 'GET') {
        return handleList(req, res);
    }

    // PATCH/DELETE /sessions/:id
    const sessionMatch = path.match(/^\/sessions\/([a-f0-9]+)$/);
    if (sessionMatch) {
        const id = sessionMatch[1];
        if (req.method === 'PATCH')  return handleHeartbeat(req, res, id);
        if (req.method === 'DELETE') return handleUnregister(req, res, id);
    }

    send(res, 404, { error: 'Not found' });
});

server.listen(PORT, () => {
    console.log(`[Server] DrunkDrivingFights matchmaking server running on port ${PORT}`);
    console.log(`[Server] Session TTL: ${SESSION_TTL_MS / 1000}s`);
});

// Graceful shutdown
process.on('SIGTERM', () => { server.close(() => process.exit(0)); });
process.on('SIGINT',  () => { server.close(() => process.exit(0)); });
