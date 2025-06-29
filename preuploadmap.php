<?php 

define('DOCROOT', getcwd().DIRECTORY_SEPARATOR);

if (isset($_SERVER['HTTP_X_SERVER_IP']) AND isset($_SERVER['HTTP_X_SERVER_PORT']))
{
	function isLocalOrZeroIP(string $ip): bool
	{
		if ($ip === '0.0.0.0')
			return true;

		if (!filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4))
			return false;

		return 
			$ip === '127.0.0.1' ||
			str_starts_with($ip, '10.') ||
			str_starts_with($ip, '192.168.') ||
			preg_match('/^172\.(1[6-9]|2[0-9]|3[0-1])\./', $ip);
	}

	function isIssetMap(string $serverId, string $mapUrl): bool
	{
		if (!file_exists(DOCROOT . 'cache/'))
			return false;

		if (!file_exists(DOCROOT . 'cache/' . $serverId))
			return false;

		$cachedUrl = file_get_contents(DOCROOT . 'cache/' . $serverId);
		$parsedCachedUrl = @parse_url($cachedUrl);
		if (isset($parsedCachedUrl['path']))
		{
			if ($cachedUrl == $mapUrl AND file_exists(DOCROOT . $parsedCachedUrl['path']))
			{
				echo 'OK! Cache map isset!';
				return true;
			}

			unlink(DOCROOT . $parsedCachedUrl['path']);

			$cachedMapDir = implode('/', array_slice(explode('/', $parsedCachedUrl['path']), 0, -1));
			rmdir(DOCROOT . $cachedMapDir);
		}

		return false;
	}

	function downloadMap(string $serverId, string $mapUrl): void
	{
		if (!file_exists(DOCROOT . 'cache/'))
			mkdir(DOCROOT . 'cache/', 0755, true);

		$parsedMapUrl = @parse_url($mapUrl);
		if (isset($parsedMapUrl['path']))
		{
			$cachedMapDir = implode('/', array_slice(explode('/', $parsedMapUrl['path']), 0, -1));
			if (!is_dir(DOCROOT . $cachedMapDir))
				mkdir(DOCROOT . $cachedMapDir, 0755, true);

			$mapData = @file_get_contents($mapUrl);
			if (end(explode('/', $cachedMapDir)) == hash('sha256', $mapData))
			{
				file_put_contents(DOCROOT . $parsedMapUrl['path'], $mapData);
				file_put_contents(DOCROOT . 'cache/' . $serverId, $mapUrl);

				echo 'OK! Cache map create!';
				return;
			}

			echo 'ERROR! Hash in URI:' . end(explode('/', $cachedMapDir)) . '; Hash downloaded data: ' . hash('sha256', $mapData);
		}
	}

	$serverIp = isLocalOrZeroIP($_SERVER['HTTP_X_SERVER_IP']) ? $_SERVER['REMOTE_ADDR'] : $_SERVER['HTTP_X_SERVER_IP'];
	$serverPort = $_SERVER['HTTP_X_SERVER_PORT'];
	$serverId = str_replace('.', '_', $serverIp) . '_' . $serverPort;
	$mapUrl = file_get_contents('php://input');

	if (!isIssetMap($serverId, $mapUrl))
	{
		downloadMap($serverId, $mapUrl);
	}
}