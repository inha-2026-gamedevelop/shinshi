-- 되돌리면 경매 기록과 입찰 예약을 잃는다. 'Auction' 상태 출정과 Open/Closing 품목이 없을 때만 되돌린다.
-- 예약이 남아 있으면 currency_reserved를 버리는 순간 예약금이 다시 쓸 수 있는 엽전이 된다
DROP TABLE auction_bids;
DROP TABLE auctions;

DROP INDEX ix_game_sessions_auction;
ALTER TABLE game_sessions
    DROP CONSTRAINT ck_game_sessions_auction_ends_at,
    DROP COLUMN auction_ends_at;

ALTER TABLE account_wallets
    DROP CONSTRAINT ck_account_wallets_currency_reserved,
    DROP COLUMN currency_reserved;
